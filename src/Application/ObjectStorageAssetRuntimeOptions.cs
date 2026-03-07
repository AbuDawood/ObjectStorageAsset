namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Shared runtime options consumed by infrastructure services.
/// </summary>
public sealed class ObjectStorageAssetRuntimeOptions
{
    public string DefaultStorageNamespace { get; init; } = string.Empty;

    public bool PhysicallyDeleteRequestedAssets { get; init; }

    public bool PhysicallyDeleteExpiredAssets { get; init; }

    public bool EnableMaintenanceWorker { get; init; }

    public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromMinutes(15);
}
