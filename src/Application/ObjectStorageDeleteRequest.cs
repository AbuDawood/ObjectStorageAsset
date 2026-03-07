namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Delete request sent to the physical object storage provider.
/// </summary>
public sealed class ObjectStorageDeleteRequest
{
    public required string BucketName { get; init; }

    public required string ObjectKey { get; init; }

    public string? VersionId { get; init; }
}
