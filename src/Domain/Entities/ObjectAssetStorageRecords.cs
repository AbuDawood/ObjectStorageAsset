using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Domain.Entities;

public sealed class PermanentObjectAsset : ObjectAsset;
public sealed class TemporaryObjectAsset : ObjectAsset;
public sealed class ArchivedObjectAsset : ObjectAsset;

public sealed class ObjectAssetIdentity
{
    public Guid AssetId { get; set; }
    public ObjectAssetLocation Location { get; set; }
    public Guid StorageAuthorityId { get; set; }
    public string BucketName { get; set; } = "";
    public string ObjectKey { get; set; } = "";
}

public sealed class ObjectAssetNamingProfileRecord
{
    public string ProfileId { get; set; } = "";
    public int Version { get; set; }
    public int DefinitionFormatVersion { get; set; } = 1;
    public string Template { get; set; } = "";
    public string DefinitionHash { get; set; } = "";
}

public sealed class ObjectAssetUpgradeState
{
    public string Name { get; set; } = "";
    public long ProcessedCount { get; set; }
    public bool Completed { get; set; }
}
