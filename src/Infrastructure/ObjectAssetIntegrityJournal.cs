using System.Transactions;
using System.Diagnostics;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetIntegrityJournal(ObjectStorageAssetPersistenceOptions persistence,
    ObjectStorageAssetSchema schema, ObjectStorageAssetRuntimeOptions options, ILogger<ObjectAssetIntegrityJournal> logger,
    TimeProvider clock)
{
    private ObjectStorageAssetDbContext Create()
    {
        var connection = new SqlConnectionStringBuilder(persistence.ConnectionString)
        { Enlist = false, Pooling = true, ConnectTimeout = 5 };
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
            ObjectVersion = result.ObjectVersion, Error = result.Error, NextAttemptAtUtc = clock.GetUtcNow()
        };
        db.IntegrityEvidence.Add(evidence);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        ObjectAssetIntegrityMetrics.Appended.Add(1);
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

    private IQueryable<IntegrityCandidate> Pending(ObjectStorageAssetDbContext db, long[]? ids,
        DateTimeOffset now, IntegrityCursor? after)
    {
        var query = db.IntegrityEvidence.AsNoTracking().Where(e => e.ResolvedAtUtc == null
            && (ids != null ? ids.Contains(e.Id) : e.NextAttemptAtUtc <= now));
        if (after is not null) query = query.Where(e => e.NextAttemptAtUtc > after.DueAtUtc
            || e.NextAttemptAtUtc == after.DueAtUtc && e.Id > after.Id);
        return query.OrderBy(e => e.NextAttemptAtUtc).ThenBy(e => e.Id).Take(options.Maintenance.Evidence.BatchSize)
            .Select(e => new IntegrityCandidate(e.Id, e.AssetId, e.NextAttemptAtUtc));
    }

    internal IntegrityApplyBatch Apply(IEnumerable<long>? evidenceIds = null)
    {
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress);
        var mirrors = new List<IntegrityMirror>(); var resolved = 0; var attempted = 0; IntegrityCursor? cursor = null;
        var timer = Stopwatch.StartNew();
        try
        {
            using var db = Create();
            db.Database.OpenConnection(); db.Database.ExecuteSqlRaw("SET LOCK_TIMEOUT 0;");
            try
            {
                var pending = Pending(db, evidenceIds?.Distinct().ToArray(), clock.GetUtcNow(), null).ToArray();
                foreach (var group in pending.GroupBy(e => e.AssetId))
                {
                    if (timer.Elapsed >= options.Maintenance.Evidence.TimeBudget) break;
                    attempted += group.Count(); db.ChangeTracker.Clear();
                    try
                    {
                        using var transaction = db.BeginAssetSave([group.Key]);
                        var ids = group.Select(e => e.Id).ToArray();
                        var rows = db.IntegrityEvidence.Where(e => ids.Contains(e.Id) && e.ResolvedAtUtc == null).ToArray();
                        var asset = db.FindReservedAsset(group.Key); var before = asset?.RowVersion.ToArray();
                        var newest = Resolve(rows, asset);
                        db.SaveReservedIntegrityChanges(); transaction!.Commit(); resolved += rows.Length;
                        RecordResolved(rows, newest);
                        if (newest is not null) mirrors.Add(new(asset!.Id, before!, asset.RowVersion.ToArray(), Result(newest)));
                    }
                    catch (Exception ex)
                    {
                        db.ChangeTracker.Clear();
                        var failedIds = group.Select(e => e.Id).ToArray();
                        var retries = db.IntegrityEvidence.AsNoTracking().Where(e => failedIds.Contains(e.Id) && e.ResolvedAtUtc == null)
                            .Select(e => new { e.Id, e.RetryCount }).ToArray();
                        foreach (var retry in retries.GroupBy(e => e.RetryCount))
                        {
                            var next = NextRetry(retry.Key); var ids = retry.Select(e => e.Id).ToArray();
                            var reason = SynchronizationReason(ex);
                            var count = db.IntegrityEvidence.Where(e => ids.Contains(e.Id) && e.ResolvedAtUtc == null && e.RetryCount == retry.Key)
                                .ExecuteUpdate(set => set.SetProperty(e => e.NextAttemptAtUtc, next)
                                    .SetProperty(e => e.RetryCount, Math.Min(30, retry.Key + 1))
                                    .SetProperty(e => e.SynchronizationError, reason));
                            ObjectAssetIntegrityMetrics.Retried.Add(count, new KeyValuePair<string, object?>("reason", IsBusy(ex) ? "busy" : "error"));
                        }
                        if (!IsBusy(ex)) logger.LogWarning(ex, "OSA validation evidence for asset {AssetId} was deferred.", group.Key);
                    }
                }
                if (pending.Length > 0) cursor = new(pending[^1].DueAtUtc, pending[^1].Id);
            }
            finally { db.Database.ExecuteSqlRaw("SET LOCK_TIMEOUT -1;"); }
            suppression.Complete();
        }
        catch (Exception ex) { logger.LogWarning(ex, "OSA validation evidence synchronization will be retried."); }
        finally { ObjectAssetIntegrityMetrics.BatchDuration.Record(timer.Elapsed.TotalMilliseconds); }
        return new(mirrors, resolved, attempted, cursor);
    }

    internal async Task<IntegrityApplyBatch> ApplyAsync(IEnumerable<long>? evidenceIds = null, CancellationToken ct = default)
        => await ApplyBatchAsync(evidenceIds?.Distinct().ToArray(), null, false, ct).ConfigureAwait(false);

    private async Task<IntegrityApplyBatch> ApplyBatchAsync(long[]? evidenceIds, IntegrityCursor? after, bool background, CancellationToken ct,
        DateTimeOffset? dueBefore = null, TimeSpan? budget = null)
    {
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        var mirrors = new List<IntegrityMirror>(); var resolved = 0; var attempted = 0; IntegrityCursor? cursor = null;
        var timer = Stopwatch.StartNew();
        try
        {
            await using var db = Create();
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0;", ct).ConfigureAwait(false);
            try
            {
                var pending = await Pending(db, evidenceIds, dueBefore ?? clock.GetUtcNow(), after).ToArrayAsync(ct).ConfigureAwait(false);
                foreach (var group in pending.GroupBy(e => e.AssetId))
                {
                    ct.ThrowIfCancellationRequested();
                    if (timer.Elapsed >= (budget ?? options.Maintenance.Evidence.TimeBudget)) break;
                    attempted += group.Count(); db.ChangeTracker.Clear();
                    try
                    {
                        await using var transaction = await db.BeginEvidenceSaveAsync(group.Key, ct).ConfigureAwait(false);
                        var ids = group.Select(e => e.Id).ToArray();
                        var rows = await db.IntegrityEvidence.Where(e => ids.Contains(e.Id) && e.ResolvedAtUtc == null).ToArrayAsync(ct).ConfigureAwait(false);
                        var asset = await db.FindAssetForUpdateAsync(group.Key, ct).ConfigureAwait(false);
                        var before = asset?.RowVersion.ToArray(); var newest = Resolve(rows, asset);
                        await db.SaveReservedIntegrityChangesAsync(ct).ConfigureAwait(false);
                        await transaction!.CommitAsync(ct).ConfigureAwait(false); resolved += rows.Length;
                        RecordResolved(rows, newest);
                        if (newest is not null) mirrors.Add(new(asset!.Id, before!, asset.RowVersion.ToArray(), Result(newest)));
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        db.ChangeTracker.Clear();
                        var failedIds = group.Select(e => e.Id).ToArray();
                        var retries = await db.IntegrityEvidence.AsNoTracking().Where(e => failedIds.Contains(e.Id) && e.ResolvedAtUtc == null)
                            .Select(e => new { e.Id, e.RetryCount }).ToArrayAsync(ct).ConfigureAwait(false);
                        foreach (var retry in retries.GroupBy(e => e.RetryCount))
                        {
                            var next = NextRetry(retry.Key); var ids = retry.Select(e => e.Id).ToArray();
                            var reason = SynchronizationReason(ex);
                            var count = await db.IntegrityEvidence.Where(e => ids.Contains(e.Id) && e.ResolvedAtUtc == null && e.RetryCount == retry.Key)
                                .ExecuteUpdateAsync(set => set.SetProperty(e => e.NextAttemptAtUtc, next)
                                    .SetProperty(e => e.RetryCount, Math.Min(30, retry.Key + 1))
                                    .SetProperty(e => e.SynchronizationError, reason), ct).ConfigureAwait(false);
                            ObjectAssetIntegrityMetrics.Retried.Add(count, new KeyValuePair<string, object?>("reason", IsBusy(ex) ? "busy" : "error"));
                        }
                        if (!IsBusy(ex)) logger.LogWarning(ex, "OSA validation evidence for asset {AssetId} was deferred.", group.Key);
                    }
                }
                if (pending.Length > 0) cursor = new(pending[^1].DueAtUtc, pending[^1].Id);
            }
            finally { await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT -1;", CancellationToken.None).ConfigureAwait(false); }
            suppression.Complete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (!background) { logger.LogWarning(ex, "OSA validation evidence synchronization will be retried."); }
        finally { ObjectAssetIntegrityMetrics.BatchDuration.Record(timer.Elapsed.TotalMilliseconds); }
        return new(mirrors, resolved, attempted, cursor);
    }

    private DateTimeOffset NextRetry(int retryCount) => clock.GetUtcNow() + TimeSpan.FromMilliseconds(Math.Min(
        options.Maintenance.Evidence.MaximumRetryDelay.TotalMilliseconds,
        options.Maintenance.Evidence.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, Math.Min(retryCount, 20))));

    private static string SynchronizationReason(Exception exception)
    {
        var reason = IsBusy(exception) ? "Busy" : exception.GetType().Name;
        return reason[..Math.Min(reason.Length, 128)];
    }

    private ObjectAssetIntegrityEvidence? Resolve(ObjectAssetIntegrityEvidence[] rows, ObjectAsset? asset)
    {
        ObjectAssetIntegrityEvidence? newest = null;
        foreach (var evidence in rows.OrderByDescending(e => e.AttemptedAtUtc).ThenByDescending(e => e.Id))
        {
            evidence.ResolvedAtUtc = clock.GetUtcNow(); evidence.SynchronizationError = null;
            if (asset is null) { evidence.Resolution = "AssetMissing"; continue; }
            if (!Applies(evidence, asset)) { evidence.Resolution = "ContentChanged"; continue; }
            if (asset.Sha256ValidationAttemptedAtUtc >= evidence.AttemptedAtUtc)
            { evidence.Resolution = "Superseded"; continue; }
            asset.RecordSha256Validation(evidence.Status, evidence.AttemptedAtUtc, evidence.CompletedAtUtc,
                evidence.ActualHash, evidence.ObjectVersion, evidence.Error);
            evidence.Resolution = "Applied"; newest = evidence;
        }
        return newest;
    }

    private static void RecordResolved(ObjectAssetIntegrityEvidence[] rows, ObjectAssetIntegrityEvidence? newest)
    {
        foreach (var resolution in rows.GroupBy(e => e.Resolution))
            ObjectAssetIntegrityMetrics.Resolved.Add(resolution.LongCount(), new KeyValuePair<string, object?>("resolution", resolution.Key));
        if (newest is not null) ObjectAssetIntegrityMetrics.AssetWrites.Add(1);
    }

    internal async Task<int> MaintainAsync(CancellationToken ct)
    {
        var settings = options.Maintenance.Evidence; var processed = 0; IntegrityCursor? cursor = null;
        var cycleStarted = clock.GetUtcNow();
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < settings.MaximumBatchesPerRun && timer.Elapsed < settings.TimeBudget; i++)
        {
            var applied = await ApplyBatchAsync(null, cursor, true, ct, cycleStarted, settings.TimeBudget - timer.Elapsed).ConfigureAwait(false);
            processed += applied.Resolved; cursor = applied.Cursor;
            if (applied.Attempted == 0) break;
        }
        // Purging receives its own budget so a busy pending queue cannot consume its capacity.
        timer.Restart();
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        await using var db = Create();
        var cutoff = clock.GetUtcNow() - options.Integrity.EvidenceRetention;
        for (var i = 0; i < settings.MaximumBatchesPerRun && timer.Elapsed < settings.TimeBudget; i++)
        {
            var removed = await db.IntegrityEvidence.Where(e => e.ResolvedAtUtc < cutoff)
                .OrderBy(e => e.ResolvedAtUtc).ThenBy(e => e.Id).Take(settings.PurgeBatchSize).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            processed += removed; ObjectAssetIntegrityMetrics.Purged.Add(removed);
            if (removed < settings.PurgeBatchSize) break;
        }
        suppression.Complete();
        return processed;
    }

    internal async Task<IntegrityBacklogSnapshot> ObserveAsync(CancellationToken ct)
    {
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        await using var db = Create(); var now = clock.GetUtcNow(); var cutoff = now - options.Integrity.EvidenceRetention;
        // Narrow indexed aggregates are sampled on a separate shared schedule, never per request.
        var pending = await db.IntegrityEvidence.Where(e => e.ResolvedAtUtc == null).GroupBy(e => 1)
            .Select(g => new { Count = g.LongCount(), Oldest = g.Min(e => e.AttemptedAtUtc) }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var expired = await db.IntegrityEvidence.LongCountAsync(e => e.ResolvedAtUtc < cutoff, ct).ConfigureAwait(false);
        suppression.Complete();
        return new(now, pending?.Count ?? 0, pending?.Oldest, expired);
    }

    private static bool IsBusy(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1222 or 1205 }) return true;
        return false;
    }
}

internal sealed record IntegrityMirror(Guid AssetId, byte[] Before, byte[] After, ObjectAssetIntegrityResult Result);
internal sealed record IntegrityCursor(DateTimeOffset DueAtUtc, long Id);
internal sealed record IntegrityCandidate(long Id, Guid AssetId, DateTimeOffset DueAtUtc);
internal sealed record IntegrityApplyBatch(IReadOnlyList<IntegrityMirror> Mirrors, int Resolved, int Attempted = 0, IntegrityCursor? Cursor = null);
