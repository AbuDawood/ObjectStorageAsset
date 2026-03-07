using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IObjectKeyStrategy, DefaultObjectKeyStrategy>();
        services.AddScoped<ObjectAssetCommandBuffer>();
        services.AddScoped<IObjectAssetSessionFactory, ObjectAssetSessionFactory>();
        services.AddScoped<IObjectAssetCoordinator, ObjectAssetCoordinator>();
        services.AddScoped<IObjectAssetReader, ObjectAssetReader>();
        services.AddScoped<IObjectAssetContentReader, ObjectAssetContentReader>();
        services.AddScoped<IObjectAssetMaintenanceService, ObjectAssetMaintenanceService>();
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
