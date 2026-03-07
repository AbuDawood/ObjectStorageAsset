using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetLifecycleManager(
    ObjectStorageAssetDbContext objectStorageAssetDbContext,
    IObjectStorageProvider objectStorageProvider,
    IObjectStorageBucketResolver bucketResolver,
    ObjectStorageAssetRuntimeOptions options,
    IObjectKeyStrategy objectKeyStrategy)
{
    private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;
    private readonly IObjectStorageProvider _objectStorageProvider = objectStorageProvider;
    private readonly IObjectStorageBucketResolver _bucketResolver = bucketResolver;
    private readonly ObjectStorageAssetRuntimeOptions _options = options;
    private readonly IObjectKeyStrategy _objectKeyStrategy = objectKeyStrategy;

    public async Task<ObjectAsset> CreateAndUploadOwnerBoundAssetAsync(
        ObjectAssetOwnerDefinition ownerDefinition,
        object normalizedOwnerKey,
        ObjectAssetSlot slot,
        ObjectAssetBufferedContent content,
        CancellationToken cancellationToken)
    {
        var asset = CreatePendingManagedAsset(ownerDefinition, slot, content);
        ObjectAssetOwnerKeyAdapter.BindOwner(asset, normalizedOwnerKey);

        await PersistAndUploadAsync(asset, content, cancellationToken).ConfigureAwait(false);
        return asset;
    }

    public async Task<ObjectAsset> CreateAndUploadTemporaryAssetAsync(
        ObjectAssetOwnerDefinition ownerDefinition,
        ObjectAssetSlot slot,
        Guid temporaryBindingId,
        DateTimeOffset? temporaryBindingExpiresAtUtc,
        ObjectAssetBufferedContent content,
        CancellationToken cancellationToken)
    {
        var asset = CreatePendingManagedAsset(ownerDefinition, slot, content);
        asset.BindTemporary(
            ownerDefinition.OwnerType,
            slot.Name,
            slot.Multiplicity,
            temporaryBindingId,
            temporaryBindingExpiresAtUtc);

        await PersistAndUploadAsync(asset, content, cancellationToken).ConfigureAwait(false);
        return asset;
    }

    public async Task DeleteAssetAsync(
        ObjectAsset asset,
        bool physicallyDelete,
        bool rethrowOnFailure,
        CancellationToken cancellationToken)
    {
        if (asset.OwnershipMode != ObjectAssetOwnershipMode.Managed || !physicallyDelete)
        {
            asset.MarkDeleted(DateTimeOffset.UtcNow, physicallyDeleted: false);
            await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        asset.MarkPendingDelete(DateTimeOffset.UtcNow);
        await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _objectStorageProvider.DeleteObjectAsync(
                    new ObjectStorageDeleteRequest
                    {
                        BucketName = asset.BucketName,
                        ObjectKey = asset.ObjectKey,
                        VersionId = asset.ProviderVersionId
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            asset.MarkDeleted(DateTimeOffset.UtcNow, physicallyDeleted: true);
            await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            asset.MarkDeleteFailed(DateTimeOffset.UtcNow, ex.Message);
            await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (rethrowOnFailure)
            {
                throw;
            }
        }
    }

    public async Task<ObjectAssetTemporaryBindingFinalizationResult> FinalizeTemporaryBindingAsync(
        ObjectAssetOwnerDefinition ownerDefinition,
        object normalizedOwnerKey,
        Guid temporaryBindingId,
        CancellationToken cancellationToken)
    {
        if (temporaryBindingId == Guid.Empty)
        {
            throw new InvalidOperationException("Temporary binding finalization requires a non-empty binding id.");
        }

        var assets = await BuildTemporaryBindingQuery(ownerDefinition, temporaryBindingId)
            .Where(x => x.Status == ObjectAssetStatus.Active)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (assets.Count == 0)
        {
            return new ObjectAssetTemporaryBindingFinalizationResult
            {
                TemporaryBindingId = temporaryBindingId,
                AssetIds = [],
                FinalizedCount = 0
            };
        }

        foreach (var group in assets.GroupBy(x => new { x.SlotName, x.SlotMultiplicity }))
        {
            if (group.Key.SlotMultiplicity == ObjectAssetSlotMultiplicity.Single)
            {
                var existingAssets = await BuildOwnerSlotQuery(
                        ownerDefinition,
                        normalizedOwnerKey,
                        group.Key.SlotName)
                    .Where(x => x.Status != ObjectAssetStatus.Deleted)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var existingAsset in existingAssets)
                {
                    await DeleteAssetAsync(
                            existingAsset,
                            _options.PhysicallyDeleteRequestedAssets,
                            rethrowOnFailure: true,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            foreach (var asset in group)
            {
                ObjectAssetOwnerKeyAdapter.BindOwner(asset, normalizedOwnerKey);
            }
        }

        await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ObjectAssetTemporaryBindingFinalizationResult
        {
            TemporaryBindingId = temporaryBindingId,
            AssetIds = assets.Select(x => x.Id).ToArray(),
            FinalizedCount = assets.Count
        };
    }

    public IQueryable<ObjectAsset> BuildOwnerSlotQuery(
        ObjectAssetOwnerDefinition ownerDefinition,
        object normalizedOwnerKey,
        string slotName)
    {
        var query = _objectStorageAssetDbContext.ObjectAssets.Where(
            x => x.OwnerType == ownerDefinition.OwnerType
                 && x.SlotName == slotName
                 && x.TemporaryBindingId == null);

        return normalizedOwnerKey switch
        {
            long int64 => query.Where(x =>
                x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Int64
                && x.OwnerKeyInt64 == int64),
            Guid guid => query.Where(x =>
                x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Guid
                && x.OwnerKeyGuid == guid),
            string text => query.Where(x =>
                x.OwnerKeyKind == ObjectAssetOwnerKeyKind.String
                && x.OwnerKeyText == text),
            _ => throw new InvalidOperationException(
                $"Normalized owner key type '{normalizedOwnerKey.GetType().FullName}' is not supported.")
        };
    }

    public IQueryable<ObjectAsset> BuildTemporaryBindingQuery(
        ObjectAssetOwnerDefinition ownerDefinition,
        Guid temporaryBindingId)
    {
        return _objectStorageAssetDbContext.ObjectAssets.Where(x =>
            x.OwnerType == ownerDefinition.OwnerType
            && x.TemporaryBindingId == temporaryBindingId);
    }

    public IQueryable<ObjectAsset> BuildTemporaryBindingSlotQuery(
        ObjectAssetOwnerDefinition ownerDefinition,
        Guid temporaryBindingId,
        string slotName)
    {
        return BuildTemporaryBindingQuery(ownerDefinition, temporaryBindingId)
            .Where(x => x.SlotName == slotName);
    }

    private ObjectAsset CreatePendingManagedAsset(
        ObjectAssetOwnerDefinition ownerDefinition,
        ObjectAssetSlot slot,
        ObjectAssetBufferedContent content)
    {
        var assetId = Guid.NewGuid();
        var utcNow = DateTimeOffset.UtcNow;
        var storageNamespace = _options.DefaultStorageNamespace.Trim();

        return ObjectAsset.CreatePendingUpload(
            assetId,
            ownerDefinition.OwnerType,
            slot.Name,
            slot.Multiplicity,
            _bucketResolver.GetDefaultBucketName(),
            storageNamespace,
            _objectKeyStrategy.CreateObjectKey(new ObjectKeyContext(
                assetId,
                storageNamespace,
                content.FileName,
                content.Extension,
                utcNow)),
            content.FileName,
            content.Extension,
            content.ContentType,
            utcNow,
            content.SizeBytes,
            content.Sha256,
            content.ExpiresAtUtc,
            ObjectAssetOwnershipMode.Managed);
    }

    private async Task PersistAndUploadAsync(
        ObjectAsset asset,
        ObjectAssetBufferedContent content,
        CancellationToken cancellationToken)
    {
        _objectStorageAssetDbContext.ObjectAssets.Add(asset);
        await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var stream = new MemoryStream(content.Bytes, writable: false);
            var putResult = await _objectStorageProvider.PutObjectAsync(
                    new ObjectStoragePutRequest
                    {
                        BucketName = asset.BucketName,
                        ObjectKey = asset.ObjectKey,
                        Content = stream,
                        ContentLength = content.SizeBytes,
                        ContentType = content.ContentType,
                        Metadata = ObjectStorageSystemMetadata.Create(
                            asset.Id,
                            asset.StorageNamespace,
                            content.Sha256)
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            asset.MarkUploadSucceeded(
                DateTimeOffset.UtcNow,
                putResult.ETag,
                putResult.VersionId);
            await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            asset.MarkUploadFailed(DateTimeOffset.UtcNow, ex.Message);
            await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
