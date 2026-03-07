using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Result for one descriptor registration request.
/// </summary>
public sealed class ObjectAssetDescriptorRegistrationResult
{
    public required Guid AssetId { get; init; }

    public required ObjectAssetDescriptorRegistrationOutcome Outcome { get; init; }

    public required ObjectAssetOwnershipMode OwnershipMode { get; init; }

    public required ObjectAssetDescriptor Descriptor { get; init; }

    public IReadOnlyList<string> ConflictFields { get; init; } = [];

    public string? ConflictMessage { get; init; }
}
