using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetAvailabilityService(ObjectStorageAssetDbContext db, ObjectAssetAvailabilityStore store,
    ObjectAssetStorageResolver storage, ObjectAssetLifecycleManager lifecycle, TimeProvider clock) : IObjectAssetAvailabilityService
{
    public async Task<ObjectAssetAvailabilityResult?> GetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await db.FindAssetAsync(assetId, false, cancellationToken).ConfigureAwait(false);
        return asset is null ? null : await store.GetAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ObjectAssetAvailabilityResult?> RecheckAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await db.FindAssetAsync(assetId, false, cancellationToken).ConfigureAwait(false);
        if (asset is null) return null;
        var started = clock.GetUtcNow();
        var stat = await storage.Resolve(asset.StorageAuthorityId).StatObjectAsync(new()
        { BucketName = asset.BucketName, ObjectKey = asset.ObjectKey, VersionId = asset.ProviderVersionId }, cancellationToken).ConfigureAwait(false);
        // Provider errors propagate; only a successful, authoritative absence changes availability.
        await store.RecordAsync(asset, stat is null ? ObjectAssetContentAvailability.Missing : ObjectAssetContentAvailability.Available,
            started, stat is null ? asset.ProviderVersionId is null ? ObjectAssetMissingReason.ObjectNotFound : ObjectAssetMissingReason.VersionNotFound : null,
            cancellationToken).ConfigureAwait(false);
        return await store.GetAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ArchiveMissingAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginAssetSaveAsync([assetId], cancellationToken).ConfigureAwait(false);
        var asset = await db.FindAssetForUpdateAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (asset is null || asset.Status == ObjectAssetStatus.Deleted) return false;
        if (asset.Status != ObjectAssetStatus.Active || (await store.GetAsync(asset, cancellationToken).ConfigureAwait(false)).Availability != ObjectAssetContentAvailability.Missing)
            throw new InvalidOperationException("Only a confirmed missing active asset can be archived by this operation.");
        await lifecycle.DeleteAssetAsync(asset, false, true, cancellationToken).ConfigureAwait(false);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
