using System.Collections.Concurrent;
using System.Data.Common;
using System.Transactions;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

public sealed partial class ObjectStorageAssetDbContext
{
    internal IQueryable<ObjectAssetContentState> ReadCommittedContentStates
    {
        get
        {
            var table = "[" + _schemaName.Replace("]", "]]", StringComparison.Ordinal) + "].[ObjectAssetContentState]";
#pragma warning disable EF1002
            return ContentStates.FromSqlRaw($"SELECT * FROM {table} WITH (READCOMMITTED)");
#pragma warning restore EF1002
        }
    }
    internal IQueryable<ObjectAssetIntegrityEvidence> ReadCommittedIntegrityEvidence
    {
        get
        {
            var table = "[" + _schemaName.Replace("]", "]]", StringComparison.Ordinal) + "].[ObjectAssetIntegrityEvidence]";
            // A caller's SERIALIZABLE/range read must not retain journal locks that would block
            // the independent evidence insert. Also read newly committed evidence under SNAPSHOT.
#pragma warning disable EF1002
            return IntegrityEvidence.FromSqlRaw($"SELECT * FROM {table} WITH (READCOMMITTED)");
#pragma warning restore EF1002
        }
    }
    private static readonly IntegrityTransactionObserver IntegrityTransactionInterceptor = new();
    private static readonly IntegrityConnectionObserver IntegrityConnectionInterceptor = new();
    private readonly object _evidenceGate = new();
    private readonly List<long> _pendingEvidence = [];
    private readonly ConcurrentQueue<IntegrityMirror> _integrityMirrors = new();
    private ObjectAssetIntegrityJournal? _integrityJournal;
    private Transaction? _evidenceAmbient;
    private bool _disposingEvidence;

    internal void RegisterIntegrityEvidence(ObjectAssetIntegrityJournal journal, long evidenceId)
    {
        _integrityJournal = journal;
        lock (_evidenceGate) _pendingEvidence.Add(evidenceId);
        var ambient = Transaction.Current ?? Database.GetEnlistedTransaction();
        if (ambient is null || ReferenceEquals(ambient, _evidenceAmbient)) return;
        _evidenceAmbient = ambient;
        TransactionCompletedEventHandler? handler = null;
        handler = (_, _) =>
        {
            ambient.TransactionCompleted -= handler;
            // Ambient completion can run on another thread; queue tracker refreshes for its owner.
            FlushIntegrityEvidence(refreshTracker: false);
        };
        ambient.TransactionCompleted += handler;
    }

    private bool EvidenceTransactionActive()
    {
        try { return _evidenceAmbient?.TransactionInformation.Status == TransactionStatus.Active; }
        catch (ObjectDisposedException) { return false; }
    }

    private long[] TakeEvidence()
    {
        lock (_evidenceGate) { var ids = _pendingEvidence.ToArray(); _pendingEvidence.Clear(); return ids; }
    }

    internal void FlushIntegrityEvidence(bool refreshTracker = true)
    {
        if (_integrityJournal is null || EvidenceTransactionActive()) return;
        var ids = TakeEvidence();
        if (ids.Length == 0) return;
        foreach (var mirror in _integrityJournal.Apply(ids).Mirrors) _integrityMirrors.Enqueue(mirror);
        if (refreshTracker) DrainIntegrityMirrors();
    }

    internal async Task FlushIntegrityEvidenceAsync()
    {
        if (_integrityJournal is null || EvidenceTransactionActive()) return;
        var ids = TakeEvidence();
        if (ids.Length == 0) return;
        var applied = await _integrityJournal.ApplyAsync(ids).ConfigureAwait(false);
        foreach (var mirror in applied.Mirrors) _integrityMirrors.Enqueue(mirror);
        DrainIntegrityMirrors();
    }

    private void DrainIntegrityMirrors()
    {
        if (_disposingEvidence || _integrityMirrors.IsEmpty) return;
        while (_integrityMirrors.TryDequeue(out var mirror))
        {
            var tracked = ChangeTracker.Entries<ObjectAsset>().FirstOrDefault(e => e.Entity.Id == mirror.AssetId
                && e.State is EntityState.Unchanged or EntityState.Modified);
            if (tracked is null || !tracked.Property(x => x.RowVersion).OriginalValue.SequenceEqual(mirror.Before)) continue;
            var fields = new (string Name, object? Value)[]
            {
                (nameof(ObjectAsset.Sha256ValidationStatus), mirror.Result.Status),
                (nameof(ObjectAsset.Sha256ValidationAttemptedAtUtc), mirror.Result.AttemptedAtUtc),
                (nameof(ObjectAsset.Sha256ValidationCompletedAtUtc), mirror.Result.CompletedAtUtc),
                (nameof(ObjectAsset.Sha256ValidationActualHash), mirror.Result.ActualHash),
                (nameof(ObjectAsset.Sha256ValidationObjectVersion), mirror.Result.ObjectVersion),
                (nameof(ObjectAsset.Sha256ValidationError), mirror.Result.Error),
                (nameof(ObjectAsset.RowVersion), mirror.After)
            };
            foreach (var field in fields)
            {
                var property = tracked.Property(field.Name);
                if (property.IsModified) continue; // Preserve unrelated unsaved caller changes.
                property.CurrentValue = field.Value; property.OriginalValue = field.Value; property.IsModified = false;
            }
        }
    }

    internal ObjectAsset? FindReservedAsset(Guid id)
    {
        var identity = AssetIdentities.AsNoTracking().SingleOrDefault(x => x.AssetId == id);
        IQueryable<ObjectAsset> query = identity?.Location switch
        {
            ObjectAssetLocation.Main => PermanentAssets,
            ObjectAssetLocation.Temporary => TemporaryAssets,
            ObjectAssetLocation.Archive => ArchivedAssets,
            _ => ObjectAssets.Where(_ => false)
        };
        return identity is null ? null : query.SingleOrDefault(x => x.Id == id);
    }

    internal Task<IDbContextTransaction?> BeginEvidenceSaveAsync(Guid id, CancellationToken ct) => BeginAssetSaveAsync([id], ct);

    // The journal already holds the identity lock and only changes validation/evidence properties.
    internal int SaveReservedIntegrityChanges() => base.SaveChanges(true);
    internal Task<int> SaveReservedIntegrityChangesAsync(CancellationToken ct) => base.SaveChangesAsync(true, ct);

    public override void Dispose()
    {
        _disposingEvidence = true;
        base.Dispose();
        FlushIntegrityEvidence(refreshTracker: false);
    }

    public override async ValueTask DisposeAsync()
    {
        _disposingEvidence = true;
        await base.DisposeAsync().ConfigureAwait(false);
        await FlushIntegrityEvidenceAsync().ConfigureAwait(false);
    }
}

internal sealed class IntegrityTransactionObserver : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => (eventData.Context as ObjectStorageAssetDbContext)?.FlushIntegrityEvidence();
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        => (eventData.Context as ObjectStorageAssetDbContext)?.FlushIntegrityEvidenceAsync() ?? Task.CompletedTask;
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => (eventData.Context as ObjectStorageAssetDbContext)?.FlushIntegrityEvidence();
    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        => (eventData.Context as ObjectStorageAssetDbContext)?.FlushIntegrityEvidenceAsync() ?? Task.CompletedTask;
}

internal sealed class IntegrityConnectionObserver : DbConnectionInterceptor
{
    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
        => (eventData.Context as ObjectStorageAssetDbContext)?.FlushIntegrityEvidence();
    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
        => (eventData.Context as ObjectStorageAssetDbContext)?.FlushIntegrityEvidenceAsync() ?? Task.CompletedTask;
}
