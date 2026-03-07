namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Summary projection for one owner/slot pair.
/// </summary>
public sealed class ObjectAssetSlotSummaryDto
{
    public bool HasAny { get; init; }

    public int Count { get; init; }

    public ObjectAssetReferenceDto? First { get; init; }
}
