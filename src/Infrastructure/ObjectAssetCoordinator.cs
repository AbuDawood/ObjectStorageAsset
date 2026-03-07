using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetCoordinator(
    ObjectAssetCommandBuffer commandBuffer,
    ObjectStorageAssetDbContext objectStorageAssetDbContext,
    ObjectAssetLifecycleManager lifecycleManager,
    ObjectStorageAssetRuntimeOptions options)
    : IObjectAssetCoordinator
{
    private readonly ObjectAssetCommandBuffer _commandBuffer = commandBuffer;
    private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;
    private readonly ObjectAssetLifecycleManager _lifecycleManager = lifecycleManager;
    private readonly ObjectStorageAssetRuntimeOptions _options = options;

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
        var existingAssets = await _lifecycleManager.BuildOwnerSlotQuery(command.OwnerDefinition, normalizedOwnerKey, command.Slot.Name)
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

        await _lifecycleManager.CreateAndUploadOwnerBoundAssetAsync(
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

        return _lifecycleManager.CreateAndUploadOwnerBoundAssetAsync(
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

    private async Task DeleteAssetAsync(
        ObjectAsset asset,
        bool physicallyDelete,
        bool rethrowOnFailure,
        CancellationToken cancellationToken)
    {
        await _lifecycleManager.DeleteAssetAsync(
                asset,
                physicallyDelete,
                rethrowOnFailure,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static object ResolveOwnerKey(ObjectAssetOwnerDefinition ownerDefinition, object owner)
    {
        var ownerKey = ownerDefinition.GetOwnerKey(owner);
        return ObjectAssetOwnerKeyAdapter.NormalizeKey(ownerKey, ownerDefinition.OwnerKeyType);
    }
}
