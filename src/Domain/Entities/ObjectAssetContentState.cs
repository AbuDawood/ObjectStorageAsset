using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Domain.Entities;

/// <summary>One current observation per asset, independent of lifecycle and SHA validation.</summary>
public sealed class ObjectAssetContentState
{
    public Guid AssetId { get; set; }
    public Guid StorageAuthorityId { get; set; }
    public string BucketName { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string? RequestedVersion { get; set; }
    public string ExpectedHash { get; set; } = "";
    public long ExpectedSize { get; set; }
    public ObjectAssetContentAvailability Availability { get; set; }
    public DateTimeOffset CheckedAtUtc { get; set; }
    public DateTimeOffset? MissingSinceUtc { get; set; }
    public ObjectAssetMissingReason? MissingReason { get; set; }
}
