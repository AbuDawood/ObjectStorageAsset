namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Read request sent to the physical object storage provider.
/// </summary>
public sealed class ObjectStorageGetRequest
{
    public required string BucketName { get; init; }

    public required string ObjectKey { get; init; }

    public string? VersionId { get; init; }
}
