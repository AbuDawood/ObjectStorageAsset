namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Read result returned by the physical object storage provider.
/// </summary>
public sealed class ObjectStorageGetResult
{
    public required string BucketName { get; init; }

    public required string ObjectKey { get; init; }

    public required Stream Content { get; init; }

    public string ContentType { get; init; } = "application/octet-stream";

    public string? ETag { get; init; }

    public string? VersionId { get; init; }
}
