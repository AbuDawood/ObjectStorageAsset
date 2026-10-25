using System.Linq.Expressions;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetPagedReader(ObjectStorageAssetDbContext db, IObjectAssetBindingCatalog catalog,
    ObjectAssetIntegrityService integrity, ObjectStorageAssetRuntimeOptions options, ObjectAssetStorageResolver storage,
    ObjectAssetIntegrityJournal journal)
    : IObjectAssetPagedReader, IObjectAssetReconciliationReader
{
    private Expression<Func<ObjectAsset, ObjectAssetPageItem>> Projection => x => new(
        x.Id, x.OriginalFileName, x.ContentType, x.SizeBytes, x.CreatedAtUtc, x.Status, x.BindingKind,
        db.ReadCommittedIntegrityEvidence.Where(e => e.AssetId == x.Id
            && (e.Resolution == null || e.Resolution == "Applied" || e.Resolution == "Superseded") && e.ExpectedHash == x.Sha256
            && e.ExpectedSize == x.SizeBytes && e.RequestedVersion == x.ProviderVersionId && e.StorageAuthorityId == x.StorageAuthorityId
            && e.BucketName == EF.Functions.Collate(x.BucketName, "Latin1_General_100_BIN2")
            && e.ObjectKey == EF.Functions.Collate(x.ObjectKey, "Latin1_General_100_BIN2")
            && (!x.Sha256ValidationAttemptedAtUtc.HasValue || e.AttemptedAtUtc > x.Sha256ValidationAttemptedAtUtc))
            .OrderByDescending(e => e.AttemptedAtUtc).ThenByDescending(e => e.Id)
            .Select(e => (Sha256ValidationStatus?)e.Status).FirstOrDefault() ?? x.Sha256ValidationStatus, x.DeletedAtUtc);

    public Task<ObjectAssetPage> GetOwnerPageAsync<T>(T owner, ObjectAssetSlot slot, ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = catalog.GetOwner<T>(); definition.GetRequiredSlot(slot);
        var key = ObjectAssetOwnerKeyAdapter.NormalizeKey(definition.GetOwnerKey(owner!), definition.OwnerKeyType);
        var query = db.PermanentAssets.Cast<ObjectAsset>().Where(x => x.OwnerType == definition.OwnerType && x.SlotName == slot.Name
            && x.BindingKind == ObjectAssetBindingKind.LocalOwner && x.Status == ObjectAssetStatus.Active);
        query = key switch
        {
            long value => query.Where(x => x.OwnerKeyInt64 == value),
            Guid value => query.Where(x => x.OwnerKeyGuid == value),
            string value => query.Where(x => x.OwnerKeyText == value),
            _ => throw new InvalidOperationException("Unsupported owner key.")
        };
        return PageAsync(query, request, false, cancellationToken);
    }

    public Task<ObjectAssetPage> GetTemporaryPageAsync(Guid temporaryBindingId, ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default)
        => PageAsync(db.TemporaryAssets.Cast<ObjectAsset>().Where(x => x.TemporaryBindingId == temporaryBindingId), request, false, cancellationToken);

    public Task<ObjectAssetPage> GetExternalOwnerPageAsync(ObjectAssetOwnerReference owner, ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default)
    {
        owner = owner.Normalize();
        return PageAsync(db.PermanentAssets.Cast<ObjectAsset>().Where(x => x.BindingKind == ObjectAssetBindingKind.ExternalOwner
            && x.ExternalOwnerInstanceId == owner.InstanceId && x.ExternalOwnerType == owner.OwnerType
            && x.ExternalOwnerKeyKind == owner.KeyKind && x.ExternalOwnerKey == owner.Key && x.Status == ObjectAssetStatus.Active), request, false, cancellationToken);
    }

    public Task<ObjectAssetPage> GetArchivePageAsync(ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default)
        => PageAsync(db.ArchivedAssets.Cast<ObjectAsset>(), request, true, cancellationToken);

    private async Task<ObjectAssetPage> PageAsync(IQueryable<ObjectAsset> query, ObjectAssetPageRequest? request, bool archive, CancellationToken ct)
    {
        request ??= new();
        if (request.PageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(request), "Page size must be 1..500.");
        query = ApplyCursor(query.AsNoTracking(), request.After, archive);
        query = archive ? query.OrderBy(x => x.DeletedAtUtc).ThenBy(x => x.Id)
            : query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id);
        var rows = await query.Take(request.PageSize + 1).Select(Projection).ToListAsync(ct);
        var more = rows.Count > request.PageSize;
        if (more) rows.RemoveAt(rows.Count - 1);
        var last = rows.LastOrDefault();
        return new(rows, more && last is not null ? new(archive ? last.DeletedAtUtc ?? last.CreatedAtUtc : last.CreatedAtUtc, last.AssetId) : null);
    }

    internal static IQueryable<ObjectAsset> ApplyCursor(IQueryable<ObjectAsset> query, ObjectAssetCursor? cursor, bool archive = false)
    {
        if (cursor is null) return query;
        return archive
            ? query.Where(x => x.DeletedAtUtc > cursor.Timestamp || x.DeletedAtUtc == cursor.Timestamp && x.Id.CompareTo(cursor.AssetId) > 0)
            : query.Where(x => x.CreatedAtUtc > cursor.Timestamp || x.CreatedAtUtc == cursor.Timestamp && x.Id.CompareTo(cursor.AssetId) > 0);
    }

    public async Task<ObjectAssetRecord?> GetRecordAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await db.FindAssetAsync(assetId, false, cancellationToken);
        if (asset is null) return null;
        var file = new ObjectAssetDescriptor
        {
            AssetId = asset.Id,
            FileName = asset.OriginalFileName,
            ContentType = asset.ContentType,
            Length = asset.SizeBytes,
            Hash = asset.Sha256,
            Bucket = asset.BucketName,
            ObjectKey = asset.ObjectKey,
            CreatedAtUtc = asset.CreatedAtUtc,
            Metadata = ObjectAssetMetadataBagSerializer.Deserialize(asset.CustomMetadataJson)
        };
        var validation = asset.Sha256ValidationAttemptedAtUtc.HasValue ? new ObjectAssetIntegrityResult(asset.Id, asset.Sha256ValidationStatus,
            asset.Sha256, asset.Sha256ValidationActualHash, asset.Sha256ValidationAttemptedAtUtc.Value,
            asset.Sha256ValidationCompletedAtUtc, asset.Sha256ValidationObjectVersion, asset.Sha256ValidationError) : null;
        validation = await journal.LatestAsync(asset, db, cancellationToken).ConfigureAwait(false) ?? validation;
        return new(new(asset.Id, asset.OriginalFileName, asset.ContentType, asset.SizeBytes, asset.CreatedAtUtc, asset.Status,
                asset.BindingKind, validation?.Status ?? asset.Sha256ValidationStatus, asset.DeletedAtUtc), file,
            new(asset.StorageAuthorityId, asset.BucketName, asset.ObjectKey, asset.ProviderVersionId), validation, asset.TemporaryBindingId,
            asset.OriginInstanceId.HasValue ? new(asset.OriginInstanceId.Value, asset.OriginAssetId!.Value) : null,
            asset.ExternalOwnerInstanceId.HasValue ? new(asset.ExternalOwnerInstanceId.Value, asset.ExternalOwnerType!, asset.ExternalOwnerKeyKind!.Value, asset.ExternalOwnerKey!) : null);
    }

    public async Task<ObjectAssetContentResult?> OpenArchivedReadAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var opened = await integrity.OpenAssetAsync(assetId, options.Integrity.Triggers.HasFlag(Sha256VerificationTriggers.OnRead), ObjectAssetReadScope.Archive, cancellationToken);
        if (opened is null) return null;
        var asset = opened.Asset;
        return new()
        {
            AssetId = asset.Id,
            FileName = asset.OriginalFileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            Content = opened.Content,
            Integrity = opened.Integrity
        };
    }

    public async Task<ObjectAssetReconciliationPage> ReconcilePageAsync(ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        if (request.PageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(request));
        var query = ApplyCursor(db.ObjectAssets.AsNoTracking().Where(x => x.Status != ObjectAssetStatus.Deleted), request.After);
        var rows = await query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(request.PageSize + 1)
            .Select(x => new
            {
                x.Id,
                x.CreatedAtUtc,
                x.Status,
                x.BindingKind,
                x.StorageAuthorityId,
                x.BucketName,
                x.ObjectKey,
                x.ProviderVersionId,
                x.TemporaryBindingExpiresAtUtc
            }).ToArrayAsync(cancellationToken);
        var items = new ObjectAssetReconciliationItem[Math.Min(rows.Length, request.PageSize)];
        await Parallel.ForEachAsync(Enumerable.Range(0, items.Length), new ParallelOptions
        { MaxDegreeOfParallelism = options.Maintenance.ProviderConcurrency, CancellationToken = cancellationToken }, async (i, ct) =>
        {
            var row = rows[i];
            var missing = row.Status == ObjectAssetStatus.Active && await storage.Resolve(row.StorageAuthorityId).StatObjectAsync(
                new ObjectStorageStatRequest { BucketName = row.BucketName, ObjectKey = row.ObjectKey, VersionId = row.ProviderVersionId }, ct) is null;
            items[i] = new(row.Id, row.Status, row.BindingKind, missing, row.BindingKind == ObjectAssetBindingKind.Temporary
                && row.TemporaryBindingExpiresAtUtc <= DateTimeOffset.UtcNow);
        });
        var last = rows.Take(items.Length).LastOrDefault();
        return new(items, rows.Length > request.PageSize && last is not null ? new(last.CreatedAtUtc, last.Id) : null);
    }
}
