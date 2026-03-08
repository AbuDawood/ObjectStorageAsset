namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Stages object asset changes for one owner instance.
/// </summary>
public interface IObjectAssetSession<T>
{
    Task SetSingleAsync(
        ObjectAssetSlot slot,
        Stream content,
        string fileName,
        string? contentType,
        DateTimeOffset? expiresAtUtc = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        ObjectAssetSlot slot,
        Stream content,
        string fileName,
        string? contentType,
        DateTimeOffset? expiresAtUtc = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid assetId, CancellationToken cancellationToken = default);
}
