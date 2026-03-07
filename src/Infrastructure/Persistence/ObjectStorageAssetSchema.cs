namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

/// <summary>
/// Resolved SQL schema name for ObjectStorageAsset.
/// </summary>
public sealed class ObjectStorageAssetSchema
{
    public ObjectStorageAssetSchema(string? name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? ObjectStorageAssetDbContext.DefaultSchemaName
            : name.Trim();
    }

    public string Name { get; }
}
