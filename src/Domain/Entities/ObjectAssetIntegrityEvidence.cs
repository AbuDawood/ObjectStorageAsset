using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Domain.Entities;

public sealed class ObjectAssetIntegrityEvidence
{
    public long Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid StorageAuthorityId { get; set; }
    public string BucketName { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string ExpectedHash { get; set; } = "";
    public long ExpectedSize { get; set; }
    public string? RequestedVersion { get; set; }
    public Sha256ValidationStatus Status { get; set; }
    public string? ActualHash { get; set; }
    public DateTimeOffset AttemptedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ObjectVersion { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? Resolution { get; set; }
}
