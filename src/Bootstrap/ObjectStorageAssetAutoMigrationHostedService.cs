using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset;

internal sealed class ObjectStorageAssetAutoMigrationHostedService(
    IServiceScopeFactory serviceScopeFactory)
    : IHostedService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var applied = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
        var pending = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
        if (applied.Any() && pending.Any(x => x.EndsWith("ExpandAssetLifecycle", StringComparison.Ordinal)
            || x.EndsWith("SplitAssetLifecycle", StringComparison.Ordinal)))
            throw new InvalidOperationException("OSA lifecycle upgrade requires an explicit maintenance-window EF migration. Automatic startup splitting is disabled.");
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
