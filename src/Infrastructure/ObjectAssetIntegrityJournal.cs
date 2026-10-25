using System.Transactions;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetIntegrityJournal(ObjectStorageAssetPersistenceOptions persistence,
    ObjectStorageAssetSchema schema, ObjectStorageAssetRuntimeOptions options, ILogger<ObjectAssetIntegrityJournal> logger)
{
    private ObjectStorageAssetDbContext Create()
    {
        var connection = new SqlConnectionStringBuilder(persistence.ConnectionString)
        { Enlist = false, Pooling = false, ConnectTimeout = 5 };
        return new(new DbContextOptionsBuilder<ObjectStorageAssetDbContext>()
            .UseSqlServer(connection.ConnectionString, sql => sql.CommandTimeout(5)).Options, schema);
    }

    internal async Task<ObjectAssetIntegrityEvidence> AppendAsync(ObjectAsset asset, ObjectAssetIntegrityResult result, CancellationToken ct)
    {
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        await using var db = Create();
        var evidence = new ObjectAssetIntegrityEvidence
        {
            AssetId = asset.Id, StorageAuthorityId = asset.StorageAuthorityId, BucketName = asset.BucketName,
            ObjectKey = asset.ObjectKey, ExpectedHash = result.ExpectedHash, ExpectedSize = asset.SizeBytes,
            RequestedVersion = asset.ProviderVersionId, Status = result.Status, ActualHash = result.ActualHash,
            AttemptedAtUtc = result.AttemptedAtUtc, CompletedAtUtc = result.CompletedAtUtc,
            ObjectVersion = result.ObjectVersion, Error = result.Error
        };
        db.IntegrityEvidence.Add(evidence);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        suppression.Complete();
        return evidence;
    }

    internal static ObjectAssetIntegrityResult Result(ObjectAssetIntegrityEvidence evidence) => new(evidence.AssetId,
        evidence.Status, evidence.ExpectedHash, evidence.ActualHash, evidence.AttemptedAtUtc, evidence.CompletedAtUtc,
        evidence.ObjectVersion, evidence.Error);

    internal static bool Applies(ObjectAssetIntegrityEvidence e, ObjectAsset asset) => e.AssetId == asset.Id
        && e.ExpectedHash == asset.Sha256 && e.ExpectedSize == asset.SizeBytes && e.RequestedVersion == asset.ProviderVersionId
        && e.StorageAuthorityId == asset.StorageAuthorityId && e.BucketName == asset.BucketName && e.ObjectKey == asset.ObjectKey;

    internal async Task<ObjectAssetIntegrityResult?> LatestAsync(ObjectAsset asset, ObjectStorageAssetDbContext db, CancellationToken ct)
    {
        var evidence = await db.ReadCommittedIntegrityEvidence.AsNoTracking().Where(e => e.AssetId == asset.Id
            && (e.Resolution == null || e.Resolution == "Applied" || e.Resolution == "Superseded")
            && e.ExpectedHash == asset.Sha256 && e.ExpectedSize == asset.SizeBytes && e.RequestedVersion == asset.ProviderVersionId
            && e.StorageAuthorityId == asset.StorageAuthorityId && e.BucketName == asset.BucketName && e.ObjectKey == asset.ObjectKey
            && (!asset.Sha256ValidationAttemptedAtUtc.HasValue || e.AttemptedAtUtc > asset.Sha256ValidationAttemptedAtUtc))
            .OrderByDescending(e => e.AttemptedAtUtc).ThenByDescending(e => e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return evidence is null ? null : Result(evidence);
    }

    private IQueryable<ObjectAssetIntegrityEvidence> Pending(ObjectStorageAssetDbContext db, long[]? ids) =>
        db.IntegrityEvidence.AsNoTracking().Where(e => e.ResolvedAtUtc == null && (ids == null || ids.Contains(e.Id)))
            .OrderBy(e => e.Id).Take(options.Maintenance.BatchSize);

    internal IntegrityApplyBatch Apply(IEnumerable<long>? evidenceIds = null)
    {
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress);
        var mirrors = new List<IntegrityMirror>(); var resolved = 0;
        try
        {
            using var db = Create();
            var pending = Pending(db, evidenceIds?.Distinct().ToArray()).ToArray();
            db.Database.OpenConnection(); db.Database.ExecuteSqlRaw("SET LOCK_TIMEOUT 0;");
            foreach (var evidence in pending)
            {
                db.ChangeTracker.Clear();
                try
                {
                    using var transaction = db.BeginAssetSave([evidence.AssetId]);
                    db.IntegrityEvidence.Attach(evidence);
                    var asset = db.FindReservedAsset(evidence.AssetId);
                    var before = asset?.RowVersion.ToArray();
                    var changed = Resolve(evidence, asset);
                    db.SaveChanges(); transaction!.Commit(); resolved++;
                    if (changed) mirrors.Add(new(asset!.Id, before!, asset.RowVersion.ToArray(), Result(evidence)));
                }
                catch (Exception ex) when (IsBusy(ex)) { }
                catch (Exception ex) { logger.LogWarning(ex, "OSA validation evidence {EvidenceId} remains pending.", evidence.Id); }
            }
            suppression.Complete();
        }
        catch (Exception ex) { logger.LogWarning(ex, "OSA validation evidence synchronization will be retried."); }
        return new(mirrors, resolved);
    }

    internal async Task<IntegrityApplyBatch> ApplyAsync(IEnumerable<long>? evidenceIds = null, CancellationToken ct = default)
    {
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        var mirrors = new List<IntegrityMirror>(); var resolved = 0;
        try
        {
            await using var db = Create();
            var pending = await Pending(db, evidenceIds?.Distinct().ToArray()).ToArrayAsync(ct).ConfigureAwait(false);
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0;", ct).ConfigureAwait(false);
            foreach (var evidence in pending)
            {
                db.ChangeTracker.Clear();
                try
                {
                    await using var transaction = await db.BeginEvidenceSaveAsync(evidence.AssetId, ct).ConfigureAwait(false);
                    db.IntegrityEvidence.Attach(evidence);
                    var asset = await db.FindAssetForUpdateAsync(evidence.AssetId, ct).ConfigureAwait(false);
                    var before = asset?.RowVersion.ToArray();
                    var changed = Resolve(evidence, asset);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await transaction!.CommitAsync(ct).ConfigureAwait(false); resolved++;
                    if (changed) mirrors.Add(new(asset!.Id, before!, asset.RowVersion.ToArray(), Result(evidence)));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (IsBusy(ex)) { }
                catch (Exception ex) { logger.LogWarning(ex, "OSA validation evidence {EvidenceId} remains pending.", evidence.Id); }
            }
            suppression.Complete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "OSA validation evidence synchronization will be retried."); }
        return new(mirrors, resolved);
    }

    private static bool Resolve(ObjectAssetIntegrityEvidence evidence, ObjectAsset? asset)
    {
        evidence.ResolvedAtUtc = DateTimeOffset.UtcNow;
        if (asset is null) { evidence.Resolution = "AssetMissing"; return false; }
        if (!Applies(evidence, asset)) { evidence.Resolution = "ContentChanged"; return false; }
        if (asset.Sha256ValidationAttemptedAtUtc >= evidence.AttemptedAtUtc)
        { evidence.Resolution = "Superseded"; return false; }
        asset.RecordSha256Validation(evidence.Status, evidence.AttemptedAtUtc, evidence.CompletedAtUtc,
            evidence.ActualHash, evidence.ObjectVersion, evidence.Error);
        evidence.Resolution = "Applied";
        return true;
    }

    internal async Task<int> MaintainAsync(CancellationToken ct)
    {
        var applied = await ApplyAsync(ct: ct).ConfigureAwait(false);
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        await using var db = Create();
        var cutoff = DateTimeOffset.UtcNow - options.Integrity.EvidenceRetention;
        var removed = await db.IntegrityEvidence.Where(e => e.ResolvedAtUtc < cutoff)
            .OrderBy(e => e.ResolvedAtUtc).ThenBy(e => e.Id).Take(options.Maintenance.BatchSize).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        suppression.Complete();
        return applied.Resolved + removed;
    }

    private static bool IsBusy(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1222 or 1205 }) return true;
        return false;
    }
}

internal sealed record IntegrityMirror(Guid AssetId, byte[] Before, byte[] After, ObjectAssetIntegrityResult Result);
internal sealed record IntegrityApplyBatch(IReadOnlyList<IntegrityMirror> Mirrors, int Resolved);
