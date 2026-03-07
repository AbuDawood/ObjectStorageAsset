using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetSessionFactory(
    IObjectAssetBindingCatalog bindingCatalog,
    ObjectAssetCommandBuffer commandBuffer)
    : IObjectAssetSessionFactory
{
    private readonly IObjectAssetBindingCatalog _bindingCatalog = bindingCatalog;
    private readonly ObjectAssetCommandBuffer _commandBuffer = commandBuffer;

    public IObjectAssetSession<T> For<T>(T owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var ownerDefinition = _bindingCatalog.GetOwner(typeof(T));
        return new ObjectAssetSession<T>(owner, ownerDefinition, _commandBuffer);
    }

    private sealed class ObjectAssetSession<T>(
        T owner,
        ObjectAssetOwnerDefinition ownerDefinition,
        ObjectAssetCommandBuffer commandBuffer)
        : IObjectAssetSession<T>
    {
        private readonly T _owner = owner;
        private readonly ObjectAssetOwnerDefinition _ownerDefinition = ownerDefinition;
        private readonly ObjectAssetCommandBuffer _commandBuffer = commandBuffer;

        public async Task SetSingleAsync(
            ObjectAssetSlot slot,
            Stream content,
            string fileName,
            string? contentType,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken cancellationToken = default)
        {
            var slotDefinition = _ownerDefinition.GetRequiredSlot(slot);
            if (slotDefinition.Multiplicity != ObjectAssetSlotMultiplicity.Single)
            {
                throw new InvalidOperationException(
                    $"Slot '{slot.Name}' does not allow SetSingleAsync because it is configured as '{slotDefinition.Multiplicity}'.");
            }

            var bufferedContent = await ObjectAssetBufferedContent.CreateAsync(
                    content,
                    fileName,
                    contentType,
                    expiresAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);

            _commandBuffer.Stage(new SetSingleObjectAssetCommand(
                _owner!,
                _ownerDefinition,
                slot,
                bufferedContent));
        }

        public async Task AddAsync(
            ObjectAssetSlot slot,
            Stream content,
            string fileName,
            string? contentType,
            DateTimeOffset? expiresAtUtc = null,
            CancellationToken cancellationToken = default)
        {
            var slotDefinition = _ownerDefinition.GetRequiredSlot(slot);
            if (slotDefinition.Multiplicity != ObjectAssetSlotMultiplicity.Many)
            {
                throw new InvalidOperationException(
                    $"Slot '{slot.Name}' does not allow AddAsync because it is configured as '{slotDefinition.Multiplicity}'.");
            }

            var bufferedContent = await ObjectAssetBufferedContent.CreateAsync(
                    content,
                    fileName,
                    contentType,
                    expiresAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);

            _commandBuffer.Stage(new AddObjectAssetCommand(
                _owner!,
                _ownerDefinition,
                slot,
                bufferedContent));
        }

        public Task RemoveAsync(Guid assetId, CancellationToken cancellationToken = default)
        {
            if (assetId == Guid.Empty)
            {
                throw new InvalidOperationException("Object asset removal requires a non-empty asset id.");
            }

            _commandBuffer.Stage(new RemoveObjectAssetCommand(assetId));
            return Task.CompletedTask;
        }
    }
}
