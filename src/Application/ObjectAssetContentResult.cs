namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Result returned when opening one asset stream.
/// </summary>
public sealed class ObjectAssetContentResult
{
    public required Guid AssetId { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required Stream Content { get; init; }
}
