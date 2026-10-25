using Elf.ObjectStorageAsset.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Elf.ObjectStorageAsset.Domain.Entities;
using System.Transactions;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetMaintenanceRunner(IServiceScopeFactory scopes, IObjectAssetMaintenanceLock guard,
    ObjectStorageAssetRuntimeOptions options, ILogger<ObjectAssetMaintenanceRunner> logger, TimeProvider clock) : IObjectAssetMaintenanceRunner
{
    private static readonly string[] TaskNames = ["expiry", "pending-delete", "retry-delete", "integrity-evidence", "integrity-observation"];
    private readonly SemaphoreSlim _localGate = new(1);
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await _localGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
            await using var lease = await guard.TryAcquireAsync(cancellationToken);
            if (lease is null) { ObjectAssetMaintenanceMetrics.Skipped.Add(1); logger.LogInformation("OSA maintenance cycle skipped: lock is held."); return; }
            var ct = lease.CancellationToken;
            using var scheduleScope = scopes.CreateScope();
            var schedule = scheduleScope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceSchedule>();
            var states = await schedule.LoadAsync(TaskNames, ct).ConfigureAwait(false);
            if (states.TryGetValue("integrity-observation", out var previous)) ObjectAssetIntegrityMetrics.Publish(previous);
            foreach (var state in states.Values.Where(s => s.LastSucceededAtUtc.HasValue))
                ObjectAssetMaintenanceMetrics.Succeeded(state.Name, state.LastSucceededAtUtc!.Value);
            var cycleStartedAtUtc = clock.GetUtcNow();
            var intervals = new[] { options.Maintenance.ExpiryInterval ?? options.MaintenanceInterval,
            options.Maintenance.PendingDeleteInterval ?? options.MaintenanceInterval,
            options.Maintenance.RetryInterval ?? options.MaintenanceInterval,
            options.Maintenance.IntegrityEvidenceInterval ?? options.MaintenanceInterval,
            options.Maintenance.Evidence.MetricsInterval };
            for (var i = 0; i < TaskNames.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var name = TaskNames[i];
                var state = states.GetValueOrDefault(name) ?? new ObjectAssetMaintenanceState { Name = name };
                if (state.NextRunAtUtc > clock.GetUtcNow()) continue;
                var started = clock.GetUtcNow();
                var tag = new KeyValuePair<string, object?>("task", name);
                try
                {
                    using var scope = scopes.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceTasks>();
                    var processed = i switch
                    {
                        0 => await service.ExpireCoreAsync(null, ct),
                        1 => await service.PendingCoreAsync(ct),
                        2 => await service.RetryCoreAsync(ct, cycleStartedAtUtc),
                        3 => await service.SynchronizeIntegrityEvidenceAsync(ct),
                        _ => 0
                    };
                    if (i == 4)
                    {
                        var observation = await service.ObserveIntegrityEvidenceAsync(ct).ConfigureAwait(false);
                        state.ObservedAtUtc = observation.ObservedAtUtc; state.PendingEvidenceCount = observation.Pending;
                        state.OldestPendingEvidenceAtUtc = observation.OldestPendingAtUtc; state.ExpiredEvidenceCount = observation.Expired;
                    }
                    state.NextRunAtUtc = clock.GetUtcNow() + intervals[i];
                    state.FailureCount = 0; state.LastSucceededAtUtc = clock.GetUtcNow();
                    await schedule.SaveAsync(state, ct).ConfigureAwait(false);
                    if (i == 4) ObjectAssetIntegrityMetrics.Publish(state);
                    ObjectAssetMaintenanceMetrics.Processed.Add(processed, tag);
                    ObjectAssetMaintenanceMetrics.Duration.Record((clock.GetUtcNow() - started).TotalMilliseconds, tag);
                    ObjectAssetMaintenanceMetrics.Succeeded(name, clock.GetUtcNow());
                    logger.LogInformation("OSA maintenance task {Task} completed {Count} records in {ElapsedMs}ms.", i, processed, (clock.GetUtcNow() - started).TotalMilliseconds);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    state.FailureCount = Math.Min(state.FailureCount + 1, 20);
                    var seconds = Math.Min(options.Maintenance.MaximumRetryDelay.TotalSeconds,
                        options.Maintenance.InitialRetryDelay.TotalSeconds * Math.Pow(2, state.FailureCount - 1) * (0.8 + Random.Shared.NextDouble() * 0.4));
                    state.NextRunAtUtc = clock.GetUtcNow().AddSeconds(Math.Max(0.001, seconds));
                    await schedule.SaveAsync(state, ct).ConfigureAwait(false);
                    ObjectAssetMaintenanceMetrics.Failures.Add(1, tag);
                    logger.LogError(ex, "OSA maintenance task {Task} failed; subsequent tasks will continue.", i);
                }
            }
            suppression.Complete();
        }
        finally { _localGate.Release(); }
    }
}

internal interface IObjectAssetMaintenanceTasks
{
    Task<int> ExpireCoreAsync(DateTimeOffset? utcNow, CancellationToken cancellationToken);
    Task<int> PendingCoreAsync(CancellationToken cancellationToken);
    Task<int> RetryCoreAsync(CancellationToken cancellationToken, DateTimeOffset? failedBefore = null);
    Task<int> SynchronizeIntegrityEvidenceAsync(CancellationToken cancellationToken);
    Task<IntegrityBacklogSnapshot> ObserveIntegrityEvidenceAsync(CancellationToken cancellationToken);
}
