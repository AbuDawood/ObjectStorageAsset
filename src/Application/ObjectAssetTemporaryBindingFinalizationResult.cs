namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Result for one temporary binding finalization.
/// </summary>
public sealed class ObjectAssetTemporaryBindingFinalizationResult
{
    public required Guid TemporaryBindingId { get; init; }

    public IReadOnlyList<Guid> AssetIds { get; init; } = [];

    public int FinalizedCount { get; init; }
}
