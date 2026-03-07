using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset;

internal sealed class ObjectStorageAssetMaintenanceHostedService(
    IServiceScopeFactory serviceScopeFactory,
    ObjectStorageAssetRuntimeOptions runtimeOptions)
    : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ObjectStorageAssetRuntimeOptions _runtimeOptions = runtimeOptions;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_runtimeOptions.MaintenanceInterval);

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
        var maintenanceService = scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>();

        await maintenanceService.ExpireAssetsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await maintenanceService.ProcessPendingDeletesAsync(cancellationToken).ConfigureAwait(false);
        await maintenanceService.RetryDeleteFailuresAsync(cancellationToken).ConfigureAwait(false);
    }
}
