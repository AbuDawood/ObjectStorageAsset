using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetCoordinator(
    ObjectAssetCommandBuffer commandBuffer,
    ObjectStorageAssetDbContext objectStorageAssetDbContext,
    IObjectStorageProvider objectStorageProvider,
    IObjectStorageBucketResolver bucketResolver,
    ObjectStorageAssetRuntimeOptions options,
    IObjectKeyStrategy objectKeyStrategy)
    : IObjectAssetCoordinator
{
    private readonly ObjectAssetCommandBuffer _commandBuffer = commandBuffer;
    private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;
    private readonly IObjectStorageProvider _objectStorageProvider = objectStorageProvider;
    private readonly IObjectStorageBucketResolver _bucketResolver = bucketResolver;
    private readonly ObjectStorageAssetRuntimeOptions _options = options;
    private readonly IObjectKeyStrategy _objectKeyStrategy = objectKeyStrategy;

    public async Task<int> SaveChangesWithAssetsAsync(
        DbContext hostDbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostDbContext);

        var hostChanges = await hostDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var commands = _commandBuffer.Drain();
        if (commands.Count == 0)
        {
            return hostChanges;
        }

        foreach (var command in commands)
        {
            switch (command)
            {
                case SetSingleObjectAssetCommand setSingle:
                    await HandleSetSingleAsync(setSingle, cancellationToken).ConfigureAwait(false);
                    break;
                case AddObjectAssetCommand add:
                    await HandleAddAsync(add, cancellationToken).ConfigureAwait(false);
                    break;
                case RemoveObjectAssetCommand remove:
                    await HandleRemoveAsync(remove, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported object asset command '{command.GetType().FullName}'.");
            }
        }

        return hostChanges;
    }

    private async Task HandleSetSingleAsync(
        SetSingleObjectAssetCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedOwnerKey = ResolveOwnerKey(command.OwnerDefinition, command.Owner);
        var existingAssets = await BuildOwnerSlotQuery(command.OwnerDefinition, normalizedOwnerKey, command.Slot.Name)
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

        await CreateAndUploadAssetAsync(
                command.OwnerDefinition,
                normalizedOwnerKey,
                command.Slot,
                command.Content,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task HandleAddAsync(
        AddObjectAssetCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedOwnerKey = ResolveOwnerKey(command.OwnerDefinition, command.Owner);

        return CreateAndUploadAssetAsync(
            command.OwnerDefinition,
            normalizedOwnerKey,
            command.Slot,
            command.Content,
            cancellationToken);
    }

    private async Task HandleRemoveAsync(
        RemoveObjectAssetCommand command,
        CancellationToken cancellationToken)
    {
        var asset = await _objectStorageAssetDbContext.ObjectAssets
            .FirstOrDefaultAsync(x => x.Id == command.AssetId, cancellationToken)
            .ConfigureAwait(false);

        if (asset is null || asset.Status == ObjectAssetStatus.Deleted)
        {
            return;
        }

        await DeleteAssetAsync(
                asset,
                _options.PhysicallyDeleteRequestedAssets,
                rethrowOnFailure: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CreateAndUploadAssetAsync(
        ObjectAssetOwnerDefinition ownerDefinition,
        object normalizedOwnerKey,
        ObjectAssetSlot slot,
        ObjectAssetBufferedContent content,
        CancellationToken cancellationToken)
    {
        var assetId = Guid.NewGuid();
        var utcNow = DateTimeOffset.UtcNow;
        var bucketName = _bucketResolver.GetDefaultBucketName();
        var storageNamespace = _options.DefaultStorageNamespace.Trim();
        var objectKey = _objectKeyStrategy.CreateObjectKey(new ObjectKeyContext(
            assetId,
            storageNamespace,
            content.FileName,
            content.Extension,
            utcNow));

        var asset = ObjectAsset.CreatePendingUpload(
            assetId,
            ownerDefinition.OwnerType,
            slot.Name,
            slot.Multiplicity,
            bucketName,
            storageNamespace,
            objectKey,
            content.FileName,
            content.Extension,
            content.ContentType,
            utcNow,
            content.SizeBytes,
            content.Sha256,
            content.ExpiresAtUtc);

        ObjectAssetOwnerKeyAdapter.BindOwner(asset, normalizedOwnerKey);

        _objectStorageAssetDbContext.ObjectAssets.Add(asset);
        await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var stream = new MemoryStream(content.Bytes, writable: false);
            var putResult = await _objectStorageProvider.PutObjectAsync(
                    new ObjectStoragePutRequest
                    {
                        BucketName = bucketName,
                        ObjectKey = objectKey,
                        Content = stream,
                        ContentLength = content.SizeBytes,
                        ContentType = content.ContentType,
                        Metadata = ObjectStorageSystemMetadata.Create(
                            assetId,
                            storageNamespace,
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

    private async Task DeleteAssetAsync(
        ObjectAsset asset,
        bool physicallyDelete,
        bool rethrowOnFailure,
        CancellationToken cancellationToken)
    {
        if (!physicallyDelete)
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

    private IQueryable<ObjectAsset> BuildOwnerSlotQuery(
        ObjectAssetOwnerDefinition ownerDefinition,
        object normalizedOwnerKey,
        string slotName)
    {
        var query = _objectStorageAssetDbContext.ObjectAssets.Where(
            x => x.OwnerType == ownerDefinition.OwnerType
                 && x.SlotName == slotName);

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

    private static object ResolveOwnerKey(ObjectAssetOwnerDefinition ownerDefinition, object owner)
    {
        var ownerKey = ownerDefinition.GetOwnerKey(owner);
        return ObjectAssetOwnerKeyAdapter.NormalizeKey(ownerKey, ownerDefinition.OwnerKeyType);
    }
}
