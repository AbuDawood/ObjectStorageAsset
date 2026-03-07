namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Summary row returned by the SQL query bridge for one owner/slot pair.
/// </summary>
public sealed class ObjectAssetSqlSlotSummaryRow<TKey>
{
    public required TKey OwnerKey { get; init; }

    public bool HasAny { get; init; }

    public int Count { get; init; }
}
