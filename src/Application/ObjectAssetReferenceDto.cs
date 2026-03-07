using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Metadata returned to host applications for one asset reference.
/// </summary>
public sealed class ObjectAssetReferenceDto
{
    public required Guid AssetId { get; init; }

    public required string SlotName { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required ObjectAssetStatus Status { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }
}
