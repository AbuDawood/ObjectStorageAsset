namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Provider upload result projected back into OSA.
/// </summary>
public sealed class ObjectStoragePutResult
{
    public required string BucketName { get; init; }

    public required string ObjectKey { get; init; }

    public string? ETag { get; init; }

    public string? VersionId { get; init; }
}
