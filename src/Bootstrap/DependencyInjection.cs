using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset;

/// <summary>
/// Host-facing dependency injection entry points for ObjectStorageAsset.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers ObjectStorageAsset services.
    /// </summary>
    public static IServiceCollection AddObjectStorageAsset(
        this IServiceCollection services,
        Action<ObjectStorageAssetOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ObjectStorageAssetOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "ObjectStorageAsset requires a non-empty SQL Server connection string.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultStorageNamespace))
        {
            throw new InvalidOperationException(
                "ObjectStorageAsset requires a non-empty default storage namespace.");
        }

        var bindingCatalog = BindingCatalogFactory.Create(options.BindingTypes);
        services.AddInfrastructureServices(persistence =>
        {
            persistence.ConnectionString = options.ConnectionString;
            persistence.Schema = options.Schema;
            persistence.AutoApplyMigrations = options.AutoApplyMigrations;
        });
        services.AddSingleton(options);
        services.AddSingleton(new ObjectStorageAssetRuntimeOptions
        {
            DefaultStorageNamespace = options.DefaultStorageNamespace.Trim(),
            PhysicallyDeleteRequestedAssets = options.PhysicallyDeleteRequestedAssets,
            PhysicallyDeleteExpiredAssets = options.PhysicallyDeleteExpiredAssets,
            EnableMaintenanceWorker = options.EnableMaintenanceWorker,
            MaintenanceInterval = options.MaintenanceInterval
        });
        services.AddSingleton(bindingCatalog);
        services.AddSingleton<IObjectAssetBindingCatalog>(bindingCatalog);

        if (options.AutoApplyMigrations)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ObjectStorageAssetAutoMigrationHostedService>());
        }

        if (options.EnableMaintenanceWorker)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ObjectStorageAssetMaintenanceHostedService>());
        }

        return services;
    }
}
