namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Uploads assets into a temporary binding before a real owner key exists.
/// </summary>
public interface IObjectAssetTemporarySession<T>
{
    Guid TemporaryBindingId { get; }

    Task<ObjectAssetReferenceDto> SetSingleAsync(
        ObjectAssetSlot slot,
        Stream content,
        string fileName,
        string? contentType,
        DateTimeOffset? expiresAtUtc = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    Task<ObjectAssetReferenceDto> AddAsync(
        ObjectAssetSlot slot,
        Stream content,
        string fileName,
        string? contentType,
        DateTimeOffset? expiresAtUtc = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid assetId, CancellationToken cancellationToken = default);
}
