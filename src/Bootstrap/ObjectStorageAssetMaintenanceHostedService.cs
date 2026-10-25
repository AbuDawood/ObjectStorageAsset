using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset;

internal sealed class ObjectStorageAssetMaintenanceHostedService(
    IServiceScopeFactory serviceScopeFactory,
    ObjectStorageAssetRuntimeOptions runtimeOptions,
    ILogger<ObjectStorageAssetMaintenanceHostedService> logger)
    : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ObjectStorageAssetRuntimeOptions _runtimeOptions = runtimeOptions;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervals = new[] { _runtimeOptions.MaintenanceInterval, _runtimeOptions.Maintenance.InitialRetryDelay,
            _runtimeOptions.Maintenance.ExpiryInterval ?? _runtimeOptions.MaintenanceInterval,
            _runtimeOptions.Maintenance.PendingDeleteInterval ?? _runtimeOptions.MaintenanceInterval,
            _runtimeOptions.Maintenance.RetryInterval ?? _runtimeOptions.MaintenanceInterval,
            _runtimeOptions.Maintenance.IntegrityEvidenceInterval ?? _runtimeOptions.MaintenanceInterval,
            _runtimeOptions.Maintenance.Evidence.MetricsInterval };
        using var timer = new PeriodicTimer(intervals.Min());

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceRunner>();
        try { await runner.RunOnceAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "OSA maintenance cycle failed; a later cycle will retry."); }
    }
}
