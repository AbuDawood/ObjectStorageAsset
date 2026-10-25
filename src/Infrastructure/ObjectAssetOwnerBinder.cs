using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetOwnerBinder(ObjectStorageAssetDbContext db, IObjectAssetBindingCatalog catalog,
    ObjectAssetLifecycleManager lifecycle, ObjectStorageAssetRuntimeOptions options) : IObjectAssetOwnerBinder
{
    public async Task<ObjectAssetReferenceDto> BindRegisteredAsync<T>(Guid assetId, T owner, ObjectAssetSlot slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = catalog.GetOwner<T>(); var slotDefinition = definition.GetRequiredSlot(slot);
        var key = ObjectAssetOwnerKeyAdapter.NormalizeKey(definition.GetOwnerKey(owner!), definition.OwnerKeyType);
        if (db.Database.CurrentTransaction is not null)
        {
            var ids = slotDefinition.Multiplicity == ObjectAssetSlotMultiplicity.Single
                ? await lifecycle.BuildOwnerSlotQuery(definition, key, slot.Name).AsNoTracking()
                    .Where(x => x.Status != ObjectAssetStatus.Deleted).Select(x => x.Id).ToListAsync(cancellationToken)
                : [];
            ids.Add(assetId);
            await db.ReserveAssetsForUpdateAsync(ids, cancellationToken);
        }
        var asset = await db.FindAssetAsync(assetId, true, cancellationToken) ?? throw new InvalidOperationException("Registered asset not found.");
        if (asset.Status != ObjectAssetStatus.Active || asset.BindingKind is not (ObjectAssetBindingKind.ExternalOwner or ObjectAssetBindingKind.LegacyUnresolved))
            throw new InvalidOperationException("Only active externally owned or unresolved registered assets can be bound locally.");
        if (slotDefinition.Multiplicity == ObjectAssetSlotMultiplicity.Single)
        {
            var previous = await lifecycle.BuildOwnerSlotQuery(definition, key, slot.Name)
                .Where(x => x.Status != ObjectAssetStatus.Deleted).ToArrayAsync(cancellationToken);
            foreach (var item in previous) await lifecycle.DeleteAssetAsync(item, options.PhysicallyDeleteRequestedAssets, true, cancellationToken);
        }
        asset.SetOwnerSlot(definition.OwnerType, slot.Name, slotDefinition.Multiplicity);
        ObjectAssetOwnerKeyAdapter.BindOwner(asset, key);
        await db.SaveChangesAsync(cancellationToken);
        return ObjectAssetReferenceMapper.Map(asset);
    }
}
