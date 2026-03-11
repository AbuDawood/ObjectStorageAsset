namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Reads and registers portable asset descriptors by AssetId.
/// Only finalized, non-temporary active assets can be exported as descriptors.
/// </summary>
public interface IObjectAssetRegistry
{
    Task<ObjectAssetDescriptor?> GetDescriptorAsync(
        Guid assetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, ObjectAssetDescriptor>> GetDescriptorsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);

    Task<ObjectAssetDescriptorRegistrationResult> RegisterDescriptorAsync(
        ObjectAssetDescriptorRegistrationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObjectAssetDescriptorRegistrationResult>> RegisterDescriptorsAsync(
        IReadOnlyCollection<ObjectAssetDescriptorRegistrationRequest> requests,
        CancellationToken cancellationToken = default);
}
