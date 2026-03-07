namespace Elf.ObjectStorageAsset;

/// <summary>
/// Bootstrap options for ObjectStorageAsset hosts.
/// </summary>
public sealed class ObjectStorageAssetOptions
{
    /// <summary>
    /// SQL Server connection string used by the library.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Database schema used for ObjectStorageAsset objects.
    /// </summary>
    public string Schema { get; set; } = "osa";

    /// <summary>
    /// Default storage namespace applied to new objects.
    /// </summary>
    public string DefaultStorageNamespace { get; set; } = string.Empty;

    /// <summary>
    /// When true, requested file deletions remove the physical object immediately.
    /// </summary>
    public bool PhysicallyDeleteRequestedAssets { get; set; } = true;

    /// <summary>
    /// When true, expired assets remove the physical object during expiry processing.
    /// </summary>
    public bool PhysicallyDeleteExpiredAssets { get; set; } = true;

    /// <summary>
    /// Enables the background maintenance worker.
    /// </summary>
    public bool EnableMaintenanceWorker { get; set; }

    /// <summary>
    /// Interval used by the maintenance worker when enabled.
    /// </summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// When true, applies pending migrations on startup.
    /// </summary>
    public bool AutoApplyMigrations { get; set; }

    internal List<Type> BindingTypes { get; } = [];

    /// <summary>
    /// Registers one host binding contract.
    /// </summary>
    public void AddBinding<TBinding>()
        where TBinding : class, new()
    {
        BindingTypes.Add(typeof(TBinding));
    }
}
