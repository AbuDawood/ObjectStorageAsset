using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetBindingFinalizer(
    IObjectAssetBindingCatalog bindingCatalog,
    ObjectAssetLifecycleManager lifecycleManager)
    : IObjectAssetBindingFinalizer
{
    private readonly IObjectAssetBindingCatalog _bindingCatalog = bindingCatalog;
    private readonly ObjectAssetLifecycleManager _lifecycleManager = lifecycleManager;

    public Task<ObjectAssetTemporaryBindingFinalizationResult> FinalizeTemporaryBindingAsync<T>(
        Guid temporaryBindingId,
        T owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var ownerDefinition = _bindingCatalog.GetOwner(typeof(T));
        var normalizedOwnerKey = ResolveOwnerKey(ownerDefinition, owner!);
        return _lifecycleManager.FinalizeTemporaryBindingAsync(
            ownerDefinition,
            normalizedOwnerKey,
            temporaryBindingId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ObjectAssetTemporaryBindingFinalizationResult>> FinalizeTemporaryBindingsAsync<T>(
        IReadOnlyCollection<ObjectAssetTemporaryBindingFinalizationRequest<T>> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            return [];
        }

        var ownerDefinition = _bindingCatalog.GetOwner(typeof(T));
        var results = new List<ObjectAssetTemporaryBindingFinalizationResult>(requests.Count);

        foreach (var request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Owner);

            results.Add(await _lifecycleManager.FinalizeTemporaryBindingAsync(
                    ownerDefinition,
                    ResolveOwnerKey(ownerDefinition, request.Owner!),
                    request.TemporaryBindingId,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return results;
    }

    private static object ResolveOwnerKey(ObjectAssetOwnerDefinition ownerDefinition, object owner)
    {
        var ownerKey = ownerDefinition.GetOwnerKey(owner);
        return ObjectAssetOwnerKeyAdapter.NormalizeKey(ownerKey, ownerDefinition.OwnerKeyType);
    }
}
