namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Portable metadata snapshot for one asset.
/// </summary>
public sealed class ObjectAssetDescriptor
{
    public const int CurrentVersion = 1;

    public required Guid AssetId { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long Length { get; init; }

    public required string Hash { get; init; }

    public required string Bucket { get; init; }

    public required string ObjectKey { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public int DescriptorVersion { get; init; } = CurrentVersion;
}
