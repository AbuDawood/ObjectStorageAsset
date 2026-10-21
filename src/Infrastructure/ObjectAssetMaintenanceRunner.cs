using Elf.ObjectStorageAsset.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetMaintenanceRunner(IServiceScopeFactory scopes, IObjectAssetMaintenanceLock guard,
    ObjectStorageAssetRuntimeOptions options, ILogger<ObjectAssetMaintenanceRunner> logger, TimeProvider clock) : IObjectAssetMaintenanceRunner
{
    private readonly DateTimeOffset[] _next = new DateTimeOffset[3];
    private readonly int[] _failures = new int[3];
    private readonly SemaphoreSlim _localGate = new(1);
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await _localGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            if (_next.All(next => next > clock.GetUtcNow())) return;
            await using var lease = await guard.TryAcquireAsync(cancellationToken);
            if (lease is null) { ObjectAssetMaintenanceMetrics.Skipped.Add(1); logger.LogInformation("OSA maintenance cycle skipped: lock is held."); return; }
            var ct = lease.CancellationToken;
            var cycleStartedAtUtc = clock.GetUtcNow();
            var intervals = new[] { options.Maintenance.ExpiryInterval ?? options.MaintenanceInterval,
            options.Maintenance.PendingDeleteInterval ?? options.MaintenanceInterval,
            options.Maintenance.RetryInterval ?? options.MaintenanceInterval };
            for (var i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (_next[i] > clock.GetUtcNow()) continue;
                var started = clock.GetUtcNow();
                var name = new[] { "expiry", "pending-delete", "retry-delete" }[i];
                var tag = new KeyValuePair<string, object?>("task", name);
                try
                {
                    using var scope = scopes.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceTasks>();
                    var processed = i switch
                    {
                        0 => await service.ExpireCoreAsync(null, ct),
                        1 => await service.PendingCoreAsync(ct),
                        _ => await service.RetryCoreAsync(ct, cycleStartedAtUtc)
                    };
                    _next[i] = clock.GetUtcNow() + intervals[i];
                    _failures[i] = 0;
                    ObjectAssetMaintenanceMetrics.Processed.Add(processed, tag);
                    ObjectAssetMaintenanceMetrics.Duration.Record((clock.GetUtcNow() - started).TotalMilliseconds, tag);
                    ObjectAssetMaintenanceMetrics.Succeeded(name, clock.GetUtcNow());
                    logger.LogInformation("OSA maintenance task {Task} completed {Count} records in {ElapsedMs}ms.", i, processed, (clock.GetUtcNow() - started).TotalMilliseconds);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _failures[i] = Math.Min(_failures[i] + 1, 20);
                    var seconds = Math.Min(options.Maintenance.MaximumRetryDelay.TotalSeconds,
                        options.Maintenance.InitialRetryDelay.TotalSeconds * Math.Pow(2, _failures[i] - 1) * (0.8 + Random.Shared.NextDouble() * 0.4));
                    _next[i] = clock.GetUtcNow().AddSeconds(Math.Max(0.001, seconds));
                    ObjectAssetMaintenanceMetrics.Failures.Add(1, tag);
                    logger.LogError(ex, "OSA maintenance task {Task} failed; subsequent tasks will continue.", i);
                }
            }
        }
        finally { _localGate.Release(); }
    }
}

internal interface IObjectAssetMaintenanceTasks
{
    Task<int> ExpireCoreAsync(DateTimeOffset? utcNow, CancellationToken cancellationToken);
    Task<int> PendingCoreAsync(CancellationToken cancellationToken);
    Task<int> RetryCoreAsync(CancellationToken cancellationToken, DateTimeOffset? failedBefore = null);
}
