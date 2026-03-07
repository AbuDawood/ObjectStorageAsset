namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Moves assets from temporary bindings onto concrete owners.
/// </summary>
public interface IObjectAssetBindingFinalizer
{
    Task<ObjectAssetTemporaryBindingFinalizationResult> FinalizeTemporaryBindingAsync<T>(
        Guid temporaryBindingId,
        T owner,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObjectAssetTemporaryBindingFinalizationResult>> FinalizeTemporaryBindingsAsync<T>(
        IReadOnlyCollection<ObjectAssetTemporaryBindingFinalizationRequest<T>> requests,
        CancellationToken cancellationToken = default);
}
