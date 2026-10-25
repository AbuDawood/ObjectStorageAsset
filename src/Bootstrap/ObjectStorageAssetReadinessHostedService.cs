using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elf.ObjectStorageAsset;

internal sealed class ObjectStorageAssetReadinessHostedService(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            throw new InvalidOperationException("OSA schema upgrade is incomplete. Apply the documented EF migrations before starting writers.");
        if (!await db.UpgradeState.AsNoTracking().AnyAsync(x => x.Name == "LifecycleSplitV1" && x.Completed, cancellationToken))
            throw new InvalidOperationException("OSA lifecycle split validation has not completed.");
        var runtime = scope.ServiceProvider.GetRequiredService<Elf.ObjectStorageAsset.Application.ObjectStorageAssetRuntimeOptions>();
        if (runtime.Naming.ActiveProfile is { } profile)
            await scope.ServiceProvider.GetRequiredService<Elf.ObjectStorageAsset.Application.IObjectAssetNamingService>().ActivateAsync(profile, cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
