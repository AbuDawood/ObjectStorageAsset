using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

/// <summary>
/// Infrastructure-level dependency injection setup.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure persistence services.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        Action<ObjectStorageAssetPersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ObjectStorageAssetPersistenceOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "ObjectStorageAsset persistence requires a non-empty SQL Server connection string.");
        }

        var schema = new ObjectStorageAssetSchema(options.Schema);
        services.AddSingleton(options);
        services.AddSingleton(schema);
        services.TryAddSingleton<IObjectKeyStrategy, DefaultObjectKeyStrategy>();
        services.AddSingleton<ObjectAssetStager>();
        services.AddSingleton<ObjectAssetNamingCatalog>();
        services.AddSingleton<IObjectAssetNamingService, ObjectAssetNamingService>();
        services.AddScoped<ObjectAssetStorageResolver>();
        services.AddLogging();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<ObjectAssetIntegrityService>();
        services.AddScoped<IObjectAssetIntegrityService>(sp => sp.GetRequiredService<ObjectAssetIntegrityService>());
        services.AddScoped<ObjectAssetCommandBuffer>();
        services.AddScoped<ObjectAssetLifecycleManager>();
        services.AddScoped<IObjectAssetSessionFactory, ObjectAssetSessionFactory>();
        services.AddScoped<IObjectAssetTemporarySessionFactory, ObjectAssetTemporarySessionFactory>();
        services.AddScoped<IObjectAssetCoordinator, ObjectAssetCoordinator>();
        services.AddScoped<IObjectAssetBindingFinalizer, ObjectAssetBindingFinalizer>();
        services.AddScoped<IObjectAssetOwnerBinder, ObjectAssetOwnerBinder>();
        services.AddScoped<IObjectAssetReader, ObjectAssetReader>();
        services.AddScoped<ObjectAssetPagedReader>();
        services.AddScoped<IObjectAssetPagedReader>(sp => sp.GetRequiredService<ObjectAssetPagedReader>());
        services.AddScoped<IObjectAssetReconciliationReader>(sp => sp.GetRequiredService<ObjectAssetPagedReader>());
        services.AddScoped<IObjectAssetContentReader, ObjectAssetContentReader>();
        services.AddScoped<ObjectAssetRegistry>();
        services.AddScoped<IObjectAssetRegistry>(sp => sp.GetRequiredService<ObjectAssetRegistry>());
        services.AddScoped<IObjectAssetExchangeRegistry>(sp => sp.GetRequiredService<ObjectAssetRegistry>());
        services.AddScoped<ObjectAssetMaintenanceService>();
        services.AddScoped<IObjectAssetMaintenanceService>(sp => sp.GetRequiredService<ObjectAssetMaintenanceService>());
        services.AddScoped<IObjectAssetMaintenanceTasks>(sp => sp.GetRequiredService<ObjectAssetMaintenanceService>());
        services.TryAddSingleton<IObjectAssetMaintenanceLock, SqlObjectAssetMaintenanceLock>();
        services.AddSingleton<IObjectAssetMaintenanceRunner, ObjectAssetMaintenanceRunner>();
        services.AddDbContext<ObjectStorageAssetDbContext>((provider, builder) =>
        {
            var resolvedOptions = provider.GetRequiredService<ObjectStorageAssetPersistenceOptions>();
            var resolvedSchema = provider.GetRequiredService<ObjectStorageAssetSchema>();

            builder.UseSqlServer(
                resolvedOptions.ConnectionString,
                sql => sql.MigrationsHistoryTable(
                    ObjectStorageAssetDbContext.MigrationsHistoryTableName,
                    resolvedSchema.Name));
        });

        return services;
    }
}
