namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

/// <summary>
/// Internal persistence options used by the infrastructure layer.
/// </summary>
public sealed class ObjectStorageAssetPersistenceOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Schema { get; set; } = ObjectStorageAssetDbContext.DefaultSchemaName;

    public bool AutoApplyMigrations { get; set; }
}
