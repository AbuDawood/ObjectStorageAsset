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
        options.Naming.ActiveProfile?.Validate();
        if (options.MaintenanceInterval <= TimeSpan.Zero || options.Maintenance.BatchSize < 1
            || options.Maintenance.ProviderConcurrency < 1 || options.Buffering.MaximumConcurrentStaging < 1
            || options.Maintenance.MaxAttempts < 1 || options.Maintenance.InitialRetryDelay <= TimeSpan.Zero
            || options.Maintenance.MaximumRetryDelay < options.Maintenance.InitialRetryDelay
            || options.Maintenance.ExpiryInterval <= TimeSpan.Zero || options.Maintenance.PendingDeleteInterval <= TimeSpan.Zero
            || options.Maintenance.RetryInterval <= TimeSpan.Zero || options.Maintenance.IntegrityEvidenceInterval <= TimeSpan.Zero
            || options.Integrity.EvidenceRetention <= TimeSpan.Zero
            || options.Maintenance.Evidence is null || options.Maintenance.Evidence.BatchSize < 1
            || options.Maintenance.Evidence.PurgeBatchSize < 1 || options.Maintenance.Evidence.MaximumBatchesPerRun < 1
            || options.Maintenance.Evidence.TimeBudget <= TimeSpan.Zero || options.Maintenance.Evidence.MetricsInterval <= TimeSpan.Zero
            || options.Maintenance.Evidence.InitialRetryDelay <= TimeSpan.Zero
            || options.Maintenance.Evidence.MaximumRetryDelay < options.Maintenance.Evidence.InitialRetryDelay
            || options.Buffering.MemoryThresholdBytes < 0 || options.Buffering.MaximumMemoryBytes < 0
            || options.Buffering.MaximumStagedFiles < 1
            || !Enum.IsDefined(options.Integrity.FailureBehavior)
            || (options.Integrity.Triggers & ~(Sha256VerificationTriggers.OnRead | Sha256VerificationTriggers.AfterUpload)) != 0
            || options.Buffering.MaximumFileBytes < 1 || options.Buffering.MaximumStagedBytes < 1)
            throw new InvalidOperationException("OSA processing limits and intervals must be positive.");
        services.AddInfrastructureServices(persistence =>
        {
            persistence.ConnectionString = options.ConnectionString;
            persistence.Schema = options.Schema;
            persistence.AutoApplyMigrations = options.AutoApplyMigrations;
        });
        services.AddSingleton(options);
        services.AddSingleton(new ObjectStorageAssetRuntimeOptions
        {
            LocalInstanceId = options.LocalInstanceId,
            StorageAuthorityId = options.StorageAuthorityId,
            StorageAuthorityAliases = new Dictionary<Guid, string>(options.StorageAuthorityAliases),
            Maintenance = options.Maintenance,
            Buffering = options.Buffering,
            Integrity = options.Integrity,
            Naming = options.Naming,
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

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ObjectStorageAssetReadinessHostedService>());

        if (options.EnableMaintenanceWorker)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ObjectStorageAssetMaintenanceHostedService>());
        }

        return services;
    }
}
