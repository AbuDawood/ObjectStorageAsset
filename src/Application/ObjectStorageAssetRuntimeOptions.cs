namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Shared runtime options consumed by infrastructure services.
/// </summary>
public sealed class ObjectStorageAssetRuntimeOptions
{
    public Guid LocalInstanceId { get; init; }
    public Guid StorageAuthorityId { get; init; }
    public IReadOnlyDictionary<Guid, string> StorageAuthorityAliases { get; init; } = new Dictionary<Guid, string>();
    public ObjectAssetMaintenanceOptions Maintenance { get; init; } = new();
    public ObjectAssetBufferOptions Buffering { get; init; } = new();
    public ObjectAssetIntegrityOptions Integrity { get; init; } = new();
    public ObjectKeyNamingOptions Naming { get; init; } = new();
    public string DefaultStorageNamespace { get; init; } = string.Empty;

    public bool PhysicallyDeleteRequestedAssets { get; init; }

    public bool PhysicallyDeleteExpiredAssets { get; init; }

    public bool EnableMaintenanceWorker { get; init; }

    public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromMinutes(15);
}
