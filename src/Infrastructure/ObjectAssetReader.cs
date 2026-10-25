using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

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
                _objectStorageAssetDbContext.PermanentAssets.AsNoTracking().Cast<ObjectAsset>(),
                ownerDefinition,
                normalizedOwnerKey,
                slot.Name)
            .Where(x => x.Status == ObjectAssetStatus.Active)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(ObjectAssetReferenceMapper.Projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return asset;
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
                _objectStorageAssetDbContext.PermanentAssets.AsNoTracking().Cast<ObjectAsset>(),
                ownerDefinition,
                normalizedOwnerKey,
                slot.Name)
            .Where(x => x.Status == ObjectAssetStatus.Active)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(ObjectAssetReferenceMapper.Projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return assets;
    }

    public async Task<IReadOnlyDictionary<TKey, ObjectAssetReferenceDto>> GetSinglesAsync<T, TKey>(
        IReadOnlyCollection<TKey> ownerKeys,
        ObjectAssetSlot slot,
        CancellationToken cancellationToken = default)
    {
        var summaries = await GetSummariesAsync<T, TKey>(ownerKeys, slot, cancellationToken);
        return summaries.Where(x => x.Value.First is not null).ToDictionary(x => x.Key, x => x.Value.First!);
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
        ArgumentNullException.ThrowIfNull(ownerKeys);
        if (ownerKeys.Count == 0) return new Dictionary<TKey, ObjectAssetSlotSummaryDto>();
        var owner = _bindingCatalog.GetOwner<T>(); owner.GetRequiredSlot(slot);
        var normalized = NormalizeOwnerKeys(owner, ownerKeys);
        var result = new Dictionary<TKey, ObjectAssetSlotSummaryDto>();
        var (column, sqlType, kind) = normalized.NormalizedKeys.First() switch
        {
            long => ("OwnerKeyInt64", "bigint", 1),
            Guid => ("OwnerKeyGuid", "uniqueidentifier", 2),
            _ => ("OwnerKeyText", "nvarchar(256)", 3)
        };
        var table = "[" + _objectStorageAssetDbContext.SchemaName.Replace("]", "]]") + "].[ObjectAssets]";
        // Fixed column/type choices and an escaped schema identifier; all host values are parameters.
#pragma warning disable EF1002
        var sql = $"""
            SELECT CONVERT(nvarchar(256),k.[OwnerKey]) AS [OwnerKey],c.[Count],f.[Id] AS [AssetId],
                f.[OriginalFileName] AS [FileName],f.[ContentType],f.[SizeBytes],f.[CreatedAtUtc]
            FROM OPENJSON(@keys) WITH ([OwnerKey] {sqlType} '$') k
            CROSS APPLY (SELECT COUNT(*) AS [Count] FROM {table} a WHERE a.[OwnerType]=@owner
                AND a.[SlotName]=@slot AND a.[{column}]=k.[OwnerKey] AND a.[OwnerKeyKind]={kind}
                AND a.[Status]=2 AND a.[BindingKind]=1) c
            OUTER APPLY (SELECT TOP (1) a.[Id],a.[OriginalFileName],a.[ContentType],a.[SizeBytes],a.[CreatedAtUtc]
                FROM {table} a WHERE a.[OwnerType]=@owner AND a.[SlotName]=@slot AND a.[{column}]=k.[OwnerKey]
                AND a.[OwnerKeyKind]={kind} AND a.[Status]=2 AND a.[BindingKind]=1
                ORDER BY a.[CreatedAtUtc] DESC,a.[Id] DESC) f
            WHERE c.[Count]>0
            """;
        foreach (var batch in normalized.NormalizedKeys.Distinct().Chunk(500))
        {
            var rows = await _objectStorageAssetDbContext.Database.SqlQueryRaw<SummaryRow>(sql,
                new SqlParameter("@keys", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(batch) },
                new SqlParameter("@owner", SqlDbType.NVarChar, 128) { Value = owner.OwnerType },
                new SqlParameter("@slot", SqlDbType.NVarChar, 128) { Value = slot.Name }).ToArrayAsync(cancellationToken);
            foreach (var row in rows)
            {
                object key = kind switch { 1 => long.Parse(row.OwnerKey, CultureInfo.InvariantCulture), 2 => Guid.Parse(row.OwnerKey), _ => row.OwnerKey };
                result[normalized.OwnerKeyMap[CreateOwnerKeyToken(key)]] = new()
                {
                    HasAny = true,
                    Count = row.Count,
                    First = row.AssetId.HasValue ? new()
                    {
                        AssetId = row.AssetId.Value,
                        SlotName = slot.Name,
                        FileName = row.FileName!,
                        ContentType = row.ContentType!,
                        SizeBytes = row.SizeBytes!.Value,
                        Status = ObjectAssetStatus.Active,
                        CreatedAtUtc = row.CreatedAtUtc!.Value,
                        ExpiresAtUtc = null
                    } : null
                };
            }
        }
#pragma warning restore EF1002
        return result;
    }

    private sealed class SummaryRow
    {
        public string OwnerKey { get; set; } = "";
        public int Count { get; set; }
        public Guid? AssetId { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public long? SizeBytes { get; set; }
        public DateTimeOffset? CreatedAtUtc { get; set; }
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
                _objectStorageAssetDbContext.PermanentAssets.AsNoTracking().Cast<ObjectAsset>(),
                ownerDefinition,
                normalizedKeys.NormalizedKeys,
                slot.Name)
            .Where(x => x.Status == ObjectAssetStatus.Active)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.OwnerKeyKind,
                x.OwnerKeyInt64,
                x.OwnerKeyGuid,
                x.OwnerKeyText,
                x.CreatedAtUtc,
                Reference = new ObjectAssetReferenceDto
                {
                    AssetId = x.Id,
                    SlotName = x.SlotName,
                    FileName = x.OriginalFileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    Status = x.Status,
                    CreatedAtUtc = x.CreatedAtUtc,
                    ExpiresAtUtc = null
                }
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return assets
            .Select(asset => new OwnerReferenceRow<TKey>(
                normalizedKeys.OwnerKeyMap[CreateOwnerKeyToken(asset.OwnerKeyKind switch
                {
                    ObjectAssetOwnerKeyKind.Int64 => asset.OwnerKeyInt64!.Value,
                    ObjectAssetOwnerKeyKind.Guid => asset.OwnerKeyGuid!.Value,
                    _ => (object)asset.OwnerKeyText!
                })],
                asset.Reference,
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
            && x.BindingKind == ObjectAssetBindingKind.LocalOwner
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
            && x.BindingKind == ObjectAssetBindingKind.LocalOwner
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
