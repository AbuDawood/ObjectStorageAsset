using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetReader(
    ObjectStorageAssetDbContext objectStorageAssetDbContext,
    IObjectAssetBindingCatalog bindingCatalog)
    : IObjectAssetReader
{
    private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;
    private readonly IObjectAssetBindingCatalog _bindingCatalog = bindingCatalog;

    public async Task<ObjectAssetReferenceDto?> GetSingleAsync<T>(
        T owner,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(slot);

        var ownerDefinition = _bindingCatalog.GetOwner<T>();
        ownerDefinition.GetRequiredSlot(slot);
        var normalizedOwnerKey = NormalizeOwnerInstanceKey(ownerDefinition, owner!);

        var asset = await ApplySingleOwnerFilter(
                _objectStorageAssetDbContext.ObjectAssets.AsNoTracking(),
                ownerDefinition,
                normalizedOwnerKey,
                slot.Name)
            .Where(x => x.Status == ObjectAssetStatus.Active)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return asset is null ? null : ObjectAssetReferenceMapper.Map(asset);
    }

    public async Task<IReadOnlyList<ObjectAssetReferenceDto>> GetManyAsync<T>(
        T owner,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(slot);

        var ownerDefinition = _bindingCatalog.GetOwner<T>();
        ownerDefinition.GetRequiredSlot(slot);
        var normalizedOwnerKey = NormalizeOwnerInstanceKey(ownerDefinition, owner!);

        var assets = await ApplySingleOwnerFilter(
                _objectStorageAssetDbContext.ObjectAssets.AsNoTracking(),
                ownerDefinition,
                normalizedOwnerKey,
                slot.Name)
            .Where(x => x.Status == ObjectAssetStatus.Active)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return assets.Select(ObjectAssetReferenceMapper.Map).ToArray();
    }

    public async Task<IReadOnlyDictionary<TKey, ObjectAssetReferenceDto>> GetSinglesAsync<T, TKey>(
        IReadOnlyCollection<TKey> ownerKeys,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryByOwnersAsync<T, TKey>(ownerKeys, slot, cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<TKey, ObjectAssetReferenceDto>();
        foreach (var group in rows.GroupBy(x => x.OwnerKey))
        {
            result[group.Key] = group.OrderByDescending(x => x.CreatedAtUtc).First().Reference;
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<TKey, IReadOnlyList<ObjectAssetReferenceDto>>> GetManyByOwnersAsync<T, TKey>(
        IReadOnlyCollection<TKey> ownerKeys,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryByOwnersAsync<T, TKey>(ownerKeys, slot, cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<TKey, IReadOnlyList<ObjectAssetReferenceDto>>();
        foreach (var group in rows.GroupBy(x => x.OwnerKey))
        {
            result[group.Key] = group
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => x.Reference)
                .ToArray();
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<TKey, ObjectAssetSlotSummaryDto>> GetSummariesAsync<T, TKey>(
        IReadOnlyCollection<TKey> ownerKeys,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryByOwnersAsync<T, TKey>(ownerKeys, slot, cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<TKey, ObjectAssetSlotSummaryDto>();
        foreach (var group in rows.GroupBy(x => x.OwnerKey))
        {
            var ordered = group.OrderByDescending(x => x.CreatedAtUtc).ToArray();
            result[group.Key] = new ObjectAssetSlotSummaryDto
            {
                HasAny = ordered.Length > 0,
                Count = ordered.Length,
                First = ordered.FirstOrDefault()?.Reference
            };
        }

        return result;
    }

    private async Task<IReadOnlyList<OwnerReferenceRow<TKey>>> QueryByOwnersAsync<T, TKey>(
        IReadOnlyCollection<TKey> ownerKeys,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerKeys);
        ArgumentNullException.ThrowIfNull(slot);

        if (ownerKeys.Count == 0)
        {
            return [];
        }

        var ownerDefinition = _bindingCatalog.GetOwner<T>();
        ownerDefinition.GetRequiredSlot(slot);

        var normalizedKeys = NormalizeOwnerKeys(ownerDefinition, ownerKeys);
        var assets = await ApplyOwnerKeysFilter(
                _objectStorageAssetDbContext.ObjectAssets.AsNoTracking(),
                ownerDefinition,
                normalizedKeys.NormalizedKeys,
                slot.Name)
            .Where(x => x.Status == ObjectAssetStatus.Active)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return assets
            .Select(asset => new OwnerReferenceRow<TKey>(
                normalizedKeys.OwnerKeyMap[CreateOwnerKeyToken(asset)],
                ObjectAssetReferenceMapper.Map(asset),
                asset.CreatedAtUtc))
            .ToArray();
    }

    private static IQueryable<ObjectAsset> ApplySingleOwnerFilter(
        IQueryable<ObjectAsset> query,
        ObjectAssetOwnerDefinition ownerDefinition,
        object normalizedOwnerKey,
        string slotName)
    {
        var baseQuery = query.Where(x =>
            x.OwnerType == ownerDefinition.OwnerType
            && x.SlotName == slotName);

        return normalizedOwnerKey switch
        {
            long int64 => baseQuery.Where(x =>
                x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Int64
                && x.OwnerKeyInt64 == int64),
            Guid guid => baseQuery.Where(x =>
                x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Guid
                && x.OwnerKeyGuid == guid),
            string text => baseQuery.Where(x =>
                x.OwnerKeyKind == ObjectAssetOwnerKeyKind.String
                && x.OwnerKeyText == text),
            _ => throw new InvalidOperationException(
                $"Normalized owner key type '{normalizedOwnerKey.GetType().FullName}' is not supported.")
        };
    }

    private static IQueryable<ObjectAsset> ApplyOwnerKeysFilter(
        IQueryable<ObjectAsset> query,
        ObjectAssetOwnerDefinition ownerDefinition,
        IReadOnlyCollection<object> normalizedOwnerKeys,
        string slotName)
    {
        var baseQuery = query.Where(x =>
            x.OwnerType == ownerDefinition.OwnerType
            && x.SlotName == slotName);

        var ownerKeyType = Nullable.GetUnderlyingType(ownerDefinition.OwnerKeyType) ?? ownerDefinition.OwnerKeyType;
        if (ownerKeyType == typeof(int) || ownerKeyType == typeof(long))
        {
            var keys = normalizedOwnerKeys.Cast<long>().Distinct().ToArray();
            return baseQuery.Where(x =>
                x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Int64
                && x.OwnerKeyInt64.HasValue
                && keys.Contains(x.OwnerKeyInt64.Value));
        }

        if (ownerKeyType == typeof(Guid))
        {
            var keys = normalizedOwnerKeys.Cast<Guid>().Distinct().ToArray();
            return baseQuery.Where(x =>
                x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Guid
                && x.OwnerKeyGuid.HasValue
                && keys.Contains(x.OwnerKeyGuid.Value));
        }

        if (ownerKeyType == typeof(string))
        {
            var keys = normalizedOwnerKeys.Cast<string>().Distinct().ToArray();
            return baseQuery.Where(x =>
                x.OwnerKeyKind == ObjectAssetOwnerKeyKind.String
                && x.OwnerKeyText != null
                && keys.Contains(x.OwnerKeyText));
        }

        throw new InvalidOperationException(
            $"Owner key type '{ownerDefinition.OwnerKeyType.FullName}' is not supported.");
    }

    private static NormalizedOwnerKeys<TKey> NormalizeOwnerKeys<TKey>(
        ObjectAssetOwnerDefinition ownerDefinition,
        IReadOnlyCollection<TKey> ownerKeys)
    {
        var normalizedKeyValues = new List<object>(ownerKeys.Count);
        var ownerKeyMap = new Dictionary<string, TKey>(StringComparer.Ordinal);

        foreach (var ownerKey in ownerKeys)
        {
            var normalized = ObjectAssetOwnerKeyAdapter.NormalizeKey(ownerKey, ownerDefinition.OwnerKeyType);
            normalizedKeyValues.Add(normalized);
            ownerKeyMap[CreateOwnerKeyToken(normalized)] = ownerKey;
        }

        return new NormalizedOwnerKeys<TKey>(normalizedKeyValues, ownerKeyMap);
    }

    private static object NormalizeOwnerInstanceKey(
        ObjectAssetOwnerDefinition ownerDefinition,
        object owner)
    {
        var ownerKey = ownerDefinition.GetOwnerKey(owner);
        return ObjectAssetOwnerKeyAdapter.NormalizeKey(ownerKey, ownerDefinition.OwnerKeyType);
    }

    private static string CreateOwnerKeyToken(object normalizedOwnerKey)
    {
        return normalizedOwnerKey switch
        {
            long int64 => $"i:{int64}",
            Guid guid => $"g:{guid:N}",
            string text => $"s:{text}",
            _ => throw new InvalidOperationException(
                $"Normalized owner key type '{normalizedOwnerKey.GetType().FullName}' is not supported.")
        };
    }

    private static string CreateOwnerKeyToken(ObjectAsset asset)
    {
        return asset.OwnerKeyKind switch
        {
            ObjectAssetOwnerKeyKind.Int64 when asset.OwnerKeyInt64.HasValue => $"i:{asset.OwnerKeyInt64.Value}",
            ObjectAssetOwnerKeyKind.Guid when asset.OwnerKeyGuid.HasValue => $"g:{asset.OwnerKeyGuid.Value:N}",
            ObjectAssetOwnerKeyKind.String when asset.OwnerKeyText is not null => $"s:{asset.OwnerKeyText}",
            _ => throw new InvalidOperationException(
                $"Object asset '{asset.Id}' has an invalid owner key state.")
        };
    }

    private sealed record OwnerReferenceRow<TKey>(
        TKey OwnerKey,
        ObjectAssetReferenceDto Reference,
        DateTimeOffset CreatedAtUtc);

    private sealed record NormalizedOwnerKeys<TKey>(
        IReadOnlyCollection<object> NormalizedKeys,
        IReadOnlyDictionary<string, TKey> OwnerKeyMap);
}
