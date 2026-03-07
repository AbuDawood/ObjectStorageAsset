using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset;

/// <summary>
/// Host-facing query bridge for joining object asset metadata in LINQ.
/// </summary>
public static class ObjectAssetQueryBridgeExtensions
{
    public static ModelBuilder ApplyObjectStorageAssetQueryModel(
        this ModelBuilder modelBuilder,
        string schema = "osa")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var normalizedSchema = string.IsNullOrWhiteSpace(schema) ? "osa" : schema.Trim();
        var entity = modelBuilder.Entity<ObjectAssetQueryModel>();
        entity.ToTable("ObjectAssets", normalizedSchema, tableBuilder => tableBuilder.ExcludeFromMigrations());
        entity.HasKey(x => x.Id);
        entity.Property(x => x.OwnerType).HasMaxLength(128).IsRequired();
        entity.Property(x => x.OwnerKeyText).HasMaxLength(256);
        entity.Property(x => x.SlotName).HasMaxLength(128).IsRequired();
        entity.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
        entity.Property(x => x.ContentType).HasMaxLength(256).IsRequired();

        return modelBuilder;
    }

    public static IQueryable<ObjectAssetSqlReferenceRow<TKey>> ObjectAssetSingleRefsFor<T, TKey>(
        this DbContext dbContext,
        IObjectAssetBindingCatalog bindingCatalog,
        ObjectAssetSlot slot)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(bindingCatalog);
        ArgumentNullException.ThrowIfNull(slot);

        if (slot.Multiplicity != ObjectAssetSlotMultiplicity.Single)
        {
            throw new InvalidOperationException(
                $"ObjectAssetSingleRefsFor requires a single slot. '{slot.Name}' is '{slot.Multiplicity}'.");
        }

        var ownerDefinition = bindingCatalog.GetOwner<T>();
        ownerDefinition.GetRequiredSlot(slot);

        return ProjectReferenceQuery<TKey>(
            dbContext.Set<ObjectAssetQueryModel>()
                .AsNoTracking()
                .Where(x => x.OwnerType == ownerDefinition.OwnerType
                            && x.SlotName == slot.Name
                            && x.Status == ObjectAssetStatus.Active),
            ownerDefinition.OwnerKeyType);
    }

    public static IQueryable<ObjectAssetSqlSlotSummaryRow<TKey>> ObjectAssetSlotSummariesFor<T, TKey>(
        this DbContext dbContext,
        IObjectAssetBindingCatalog bindingCatalog,
        ObjectAssetSlot slot)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(bindingCatalog);
        ArgumentNullException.ThrowIfNull(slot);

        var ownerDefinition = bindingCatalog.GetOwner<T>();
        ownerDefinition.GetRequiredSlot(slot);

        return ProjectSummaryQuery<TKey>(
            dbContext.Set<ObjectAssetQueryModel>()
                .AsNoTracking()
                .Where(x => x.OwnerType == ownerDefinition.OwnerType
                            && x.SlotName == slot.Name
                            && x.Status == ObjectAssetStatus.Active),
            ownerDefinition.OwnerKeyType);
    }

    private static IQueryable<ObjectAssetSqlReferenceRow<TKey>> ProjectReferenceQuery<TKey>(
        IQueryable<ObjectAssetQueryModel> query,
        Type ownerKeyType)
    {
        var effectiveOwnerKeyType = Nullable.GetUnderlyingType(ownerKeyType) ?? ownerKeyType;

        if (effectiveOwnerKeyType == typeof(int))
        {
            return (IQueryable<ObjectAssetSqlReferenceRow<TKey>>)(object)query.Select(
                x => new { x.OwnerKeyKind, x.OwnerKeyInt64, x.Id, x.SlotName, x.OriginalFileName, x.ContentType, x.SizeBytes, x.Status, x.CreatedAtUtc, x.ExpiresAtUtc })
                .Where(x => x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Int64 && x.OwnerKeyInt64.HasValue)
                .Select(
                x => new ObjectAssetSqlReferenceRow<int>
                {
                    OwnerKey = (int)x.OwnerKeyInt64.Value,
                    AssetId = x.Id,
                    SlotName = x.SlotName,
                    FileName = x.OriginalFileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    Status = x.Status,
                    CreatedAtUtc = x.CreatedAtUtc,
                    ExpiresAtUtc = x.ExpiresAtUtc
                });
        }

        if (effectiveOwnerKeyType == typeof(long))
        {
            return (IQueryable<ObjectAssetSqlReferenceRow<TKey>>)(object)query.Select(
                x => new { x.OwnerKeyKind, x.OwnerKeyInt64, x.Id, x.SlotName, x.OriginalFileName, x.ContentType, x.SizeBytes, x.Status, x.CreatedAtUtc, x.ExpiresAtUtc })
                .Where(x => x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Int64 && x.OwnerKeyInt64.HasValue)
                .Select(
                x => new ObjectAssetSqlReferenceRow<long>
                {
                    OwnerKey = x.OwnerKeyInt64.Value,
                    AssetId = x.Id,
                    SlotName = x.SlotName,
                    FileName = x.OriginalFileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    Status = x.Status,
                    CreatedAtUtc = x.CreatedAtUtc,
                    ExpiresAtUtc = x.ExpiresAtUtc
                });
        }

        if (effectiveOwnerKeyType == typeof(Guid))
        {
            return (IQueryable<ObjectAssetSqlReferenceRow<TKey>>)(object)query.Select(
                x => new { x.OwnerKeyKind, x.OwnerKeyGuid, x.Id, x.SlotName, x.OriginalFileName, x.ContentType, x.SizeBytes, x.Status, x.CreatedAtUtc, x.ExpiresAtUtc })
                .Where(x => x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Guid && x.OwnerKeyGuid.HasValue)
                .Select(
                x => new ObjectAssetSqlReferenceRow<Guid>
                {
                    OwnerKey = x.OwnerKeyGuid.Value,
                    AssetId = x.Id,
                    SlotName = x.SlotName,
                    FileName = x.OriginalFileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    Status = x.Status,
                    CreatedAtUtc = x.CreatedAtUtc,
                    ExpiresAtUtc = x.ExpiresAtUtc
                });
        }

        if (effectiveOwnerKeyType == typeof(string))
        {
            return (IQueryable<ObjectAssetSqlReferenceRow<TKey>>)(object)query.Select(
                x => new { x.OwnerKeyKind, x.OwnerKeyText, x.Id, x.SlotName, x.OriginalFileName, x.ContentType, x.SizeBytes, x.Status, x.CreatedAtUtc, x.ExpiresAtUtc })
                .Where(x => x.OwnerKeyKind == ObjectAssetOwnerKeyKind.String && x.OwnerKeyText != null)
                .Select(
                x => new ObjectAssetSqlReferenceRow<string>
                {
                    OwnerKey = x.OwnerKeyText!,
                    AssetId = x.Id,
                    SlotName = x.SlotName,
                    FileName = x.OriginalFileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    Status = x.Status,
                    CreatedAtUtc = x.CreatedAtUtc,
                    ExpiresAtUtc = x.ExpiresAtUtc
                });
        }

        throw new InvalidOperationException(
            $"Owner key type '{ownerKeyType.FullName}' is not supported by the object asset query bridge.");
    }

    private static IQueryable<ObjectAssetSqlSlotSummaryRow<TKey>> ProjectSummaryQuery<TKey>(
        IQueryable<ObjectAssetQueryModel> query,
        Type ownerKeyType)
    {
        var effectiveOwnerKeyType = Nullable.GetUnderlyingType(ownerKeyType) ?? ownerKeyType;

        if (effectiveOwnerKeyType == typeof(int))
        {
            return (IQueryable<ObjectAssetSqlSlotSummaryRow<TKey>>)(object)query
                .Where(x => x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Int64 && x.OwnerKeyInt64.HasValue)
                .GroupBy(x => x.OwnerKeyInt64!.Value)
                .Select(x => new ObjectAssetSqlSlotSummaryRow<int>
                {
                    OwnerKey = (int)x.Key,
                    HasAny = x.Any(),
                    Count = x.Count()
                });
        }

        if (effectiveOwnerKeyType == typeof(long))
        {
            return (IQueryable<ObjectAssetSqlSlotSummaryRow<TKey>>)(object)query
                .Where(x => x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Int64 && x.OwnerKeyInt64.HasValue)
                .GroupBy(x => x.OwnerKeyInt64!.Value)
                .Select(x => new ObjectAssetSqlSlotSummaryRow<long>
                {
                    OwnerKey = x.Key,
                    HasAny = x.Any(),
                    Count = x.Count()
                });
        }

        if (effectiveOwnerKeyType == typeof(Guid))
        {
            return (IQueryable<ObjectAssetSqlSlotSummaryRow<TKey>>)(object)query
                .Where(x => x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Guid && x.OwnerKeyGuid.HasValue)
                .GroupBy(x => x.OwnerKeyGuid!.Value)
                .Select(x => new ObjectAssetSqlSlotSummaryRow<Guid>
                {
                    OwnerKey = x.Key,
                    HasAny = x.Any(),
                    Count = x.Count()
                });
        }

        if (effectiveOwnerKeyType == typeof(string))
        {
            return (IQueryable<ObjectAssetSqlSlotSummaryRow<TKey>>)(object)query
                .Where(x => x.OwnerKeyKind == ObjectAssetOwnerKeyKind.String && x.OwnerKeyText != null)
                .GroupBy(x => x.OwnerKeyText!)
                .Select(x => new ObjectAssetSqlSlotSummaryRow<string>
                {
                    OwnerKey = x.Key,
                    HasAny = x.Any(),
                    Count = x.Count()
                });
        }

        throw new InvalidOperationException(
            $"Owner key type '{ownerKeyType.FullName}' is not supported by the object asset query bridge.");
    }
}
