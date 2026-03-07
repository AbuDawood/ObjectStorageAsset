namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Metadata result returned by the physical object storage provider.
/// </summary>
public sealed class ObjectStorageStatResult
{
    public required string BucketName { get; init; }

    public required string ObjectKey { get; init; }

    public long? SizeBytes { get; init; }

    public string? ContentType { get; init; }

    public string? ETag { get; init; }

    public string? VersionId { get; init; }

    public DateTimeOffset? LastModifiedAtUtc { get; init; }
}
