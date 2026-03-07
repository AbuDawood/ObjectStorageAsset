namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Upload request sent to the physical object storage provider.
/// </summary>
public sealed class ObjectStoragePutRequest
{
    public required string BucketName { get; init; }

    public required string ObjectKey { get; init; }

    public required Stream Content { get; init; }

    public required long ContentLength { get; init; }

    public string ContentType { get; init; } = "application/octet-stream";

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
