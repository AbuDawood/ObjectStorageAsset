namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Reads object asset metadata without loading file bytes.
/// </summary>
public interface IObjectAssetReader
{
    Task<ObjectAssetReferenceDto?> GetSingleAsync<T>(
        T owner,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObjectAssetReferenceDto>> GetManyAsync<T>(
        T owner,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<TKey, ObjectAssetReferenceDto>> GetSinglesAsync<T, TKey>(
        IReadOnlyCollection<TKey> ownerKeys,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<TKey, IReadOnlyList<ObjectAssetReferenceDto>>> GetManyByOwnersAsync<T, TKey>(
        IReadOnlyCollection<TKey> ownerKeys,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<TKey, ObjectAssetSlotSummaryDto>> GetSummariesAsync<T, TKey>(
        IReadOnlyCollection<TKey> ownerKeys,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default);
}
