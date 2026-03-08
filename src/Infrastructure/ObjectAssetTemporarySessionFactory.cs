using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetTemporarySessionFactory(
    IObjectAssetBindingCatalog bindingCatalog,
    ObjectAssetLifecycleManager lifecycleManager,
    ObjectStorageAssetDbContext objectStorageAssetDbContext,
    ObjectStorageAssetRuntimeOptions options)
    : IObjectAssetTemporarySessionFactory
{
    private readonly IObjectAssetBindingCatalog _bindingCatalog = bindingCatalog;
    private readonly ObjectAssetLifecycleManager _lifecycleManager = lifecycleManager;
    private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;
    private readonly ObjectStorageAssetRuntimeOptions _options = options;

    public IObjectAssetTemporarySession<T> For<T>(
        Guid temporaryBindingId,
        DateTimeOffset? temporaryBindingExpiresAtUtc = null)
    {
        if (temporaryBindingId == Guid.Empty)
        {
            throw new InvalidOperationException("Temporary object asset sessions require a non-empty binding id.");
        }

        var ownerDefinition = _bindingCatalog.GetOwner(typeof(T));
        return new ObjectAssetTemporarySession<T>(
            temporaryBindingId,
            temporaryBindingExpiresAtUtc,
            ownerDefinition,
            _lifecycleManager,
            _objectStorageAssetDbContext,
            _options);
    }

    private sealed class ObjectAssetTemporarySession<T>(
        Guid temporaryBindingId,
        DateTimeOffset? temporaryBindingExpiresAtUtc,
        ObjectAssetOwnerDefinition ownerDefinition,
        ObjectAssetLifecycleManager lifecycleManager,
        ObjectStorageAssetDbContext objectStorageAssetDbContext,
        ObjectStorageAssetRuntimeOptions options)
        : IObjectAssetTemporarySession<T>
    {
        private readonly ObjectAssetOwnerDefinition _ownerDefinition = ownerDefinition;
        private readonly ObjectAssetLifecycleManager _lifecycleManager = lifecycleManager;
        private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;
        private readonly ObjectStorageAssetRuntimeOptions _options = options;

        public Guid TemporaryBindingId { get; } = temporaryBindingId;

        public async Task<ObjectAssetReferenceDto> SetSingleAsync(
            ObjectAssetSlot slot,
            Stream content,
            string fileName,
            string? contentType,
            DateTimeOffset? expiresAtUtc = null,
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var slotDefinition = _ownerDefinition.GetRequiredSlot(slot);
            if (slotDefinition.Multiplicity != ObjectAssetSlotMultiplicity.Single)
            {
                throw new InvalidOperationException(
                    $"Slot '{slot.Name}' does not allow SetSingleAsync because it is configured as '{slotDefinition.Multiplicity}'.");
            }

            var existingAssets = await _lifecycleManager.BuildTemporaryBindingSlotQuery(
                    _ownerDefinition,
                    TemporaryBindingId,
                    slot.Name)
                .Where(x => x.Status != ObjectAssetStatus.Deleted)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var existingAsset in existingAssets)
            {
                await _lifecycleManager.DeleteAssetAsync(
                        existingAsset,
                        _options.PhysicallyDeleteRequestedAssets,
                        rethrowOnFailure: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return await UploadAsync(slot, content, fileName, contentType, expiresAtUtc, metadata, cancellationToken).ConfigureAwait(false);
        }

        public Task<ObjectAssetReferenceDto> AddAsync(
            ObjectAssetSlot slot,
            Stream content,
            string fileName,
            string? contentType,
            DateTimeOffset? expiresAtUtc = null,
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var slotDefinition = _ownerDefinition.GetRequiredSlot(slot);
            if (slotDefinition.Multiplicity != ObjectAssetSlotMultiplicity.Many)
            {
                throw new InvalidOperationException(
                    $"Slot '{slot.Name}' does not allow AddAsync because it is configured as '{slotDefinition.Multiplicity}'.");
            }

            return UploadAsync(slot, content, fileName, contentType, expiresAtUtc, metadata, cancellationToken);
        }

        public async Task RemoveAsync(Guid assetId, CancellationToken cancellationToken = default)
        {
            if (assetId == Guid.Empty)
            {
                throw new InvalidOperationException("Object asset removal requires a non-empty asset id.");
            }

            var asset = await _objectStorageAssetDbContext.ObjectAssets
                .FirstOrDefaultAsync(
                    x => x.Id == assetId
                         && x.TemporaryBindingId == TemporaryBindingId
                         && x.OwnerType == _ownerDefinition.OwnerType
                         && x.Status != ObjectAssetStatus.Deleted,
                    cancellationToken)
                .ConfigureAwait(false);

            if (asset is null)
            {
                return;
            }

            await _lifecycleManager.DeleteAssetAsync(
                    asset,
                    _options.PhysicallyDeleteRequestedAssets,
                    rethrowOnFailure: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<ObjectAssetReferenceDto> UploadAsync(
            ObjectAssetSlot slot,
            Stream content,
            string fileName,
            string? contentType,
            DateTimeOffset? expiresAtUtc,
            IReadOnlyDictionary<string, string>? metadata,
            CancellationToken cancellationToken)
        {
            var bufferedContent = await ObjectAssetBufferedContent.CreateAsync(
                    content,
                    fileName,
                    contentType,
                    expiresAtUtc,
                    metadata,
                    cancellationToken)
                .ConfigureAwait(false);

            var asset = await _lifecycleManager.CreateAndUploadTemporaryAssetAsync(
                    _ownerDefinition,
                    slot,
                    TemporaryBindingId,
                    temporaryBindingExpiresAtUtc,
                    bufferedContent,
                    cancellationToken)
                .ConfigureAwait(false);

            return ObjectAssetReferenceMapper.Map(asset);
        }
    }
}
