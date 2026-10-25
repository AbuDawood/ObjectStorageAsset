using System.Collections.Concurrent;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed partial class WorkflowTests
{
    [Test]
    public async Task SynchronousSave_ShouldCompleteWithoutPostingToSingleThreadContext()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var context = new AuditSynchronizationContext();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using var db = LockContext(fixture);
                var asset = ObjectAsset.CreatePendingUpload(Guid.NewGuid(), "order", "attachments", ObjectAssetSlotMultiplicity.Many,
                    "osa-dev", "unit-tests", "sync/" + Guid.NewGuid(), "a.txt", ".txt", "text/plain", DateTimeOffset.UtcNow);
                asset.BindTemporary("order", "attachments", ObjectAssetSlotMultiplicity.Many, Guid.NewGuid(), null);
                asset.MarkUploadSucceeded(DateTimeOffset.UtcNow, "test", null);
                db.Add(asset); db.SaveChanges();
                var temporary = db.TemporaryAssets.Single(); temporary.BindOwner(42L); db.SaveChanges();
                var permanent = db.PermanentAssets.Single(); permanent.MarkDeleted(DateTimeOffset.UtcNow, false); db.SaveChanges();
                db.ArchivedAssets.Count().Should().Be(1); db.AssetIdentities.Count().Should().Be(1);
                finished.SetResult();
            }
            catch (Exception ex) { finished.SetException(ex); }
        }) { IsBackground = true };
        thread.Start();
        try
        {
            var completed = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            completed.Should().Be(finished.Task, "synchronous persistence must not need its blocked caller to pump continuations");
            await finished.Task;
            context.PostCount.Should().Be(0);
        }
        finally { context.Release(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SaveWithoutAcceptance_ShouldPreserveNormalEntityStateAndRejectMoves(bool synchronous)
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var db = LockContext(fixture);
        var asset = ObjectAsset.CreatePendingUpload(Guid.NewGuid(), "order", "files", ObjectAssetSlotMultiplicity.Many,
            "osa-dev", "unit-tests", "accept/" + Guid.NewGuid(), "a.txt", ".txt", "text/plain", DateTimeOffset.UtcNow);
        asset.BindOwner(42L); db.Add(asset);
        if (synchronous) db.SaveChanges(false); else await db.SaveChangesAsync(false);
        db.Entry(asset).State.Should().Be(EntityState.Added);
        db.ChangeTracker.AcceptAllChanges();
        asset.MarkDeleted(DateTimeOffset.UtcNow, false);
        Func<Task> move = async () => { if (synchronous) db.SaveChanges(false); else await db.SaveChangesAsync(false); };
        await move.Should().ThrowAsync<NotSupportedException>();
        await using var check = LockContext(fixture);
        (await check.PermanentAssets.CountAsync()).Should().Be(1);
    }

    [TestCase(Sha256FailureBehavior.Warning)]
    [TestCase(Sha256FailureBehavior.Guard)]
    public async Task IntegrityRead_WithCallerWriteLock_ShouldPersistEvidenceBeforeRollbackWithoutSelfBlocking(Sha256FailureBehavior behavior)
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: o =>
        { o.Integrity.FailureBehavior = behavior; o.Integrity.Triggers = Sha256VerificationTriggers.OnRead; });
        using var scope = fixture.RootProvider.CreateScope();
        var bindingId = Guid.NewGuid();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(bindingId)
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        fixture.StorageProvider.CorruptOnGet = true;
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using (var transaction = await db.Database.BeginTransactionAsync(timeout.Token))
        {
            var asset = (await db.FindAssetForUpdateAsync(uploaded.AssetId, timeout.Token))!;
            asset.MarkUploadSucceeded(DateTimeOffset.UtcNow, asset.ETag, asset.ProviderVersionId);
            await db.SaveChangesAsync(timeout.Token); // Hold actual uncommitted identity/payload write locks.
            var unsaved = ObjectAsset.CreateRegisteredDescriptor(Guid.NewGuid(), "osa-dev", "unsaved", "u.txt", "text/plain",
                0, new string('a', 64), DateTimeOffset.UtcNow, null, ObjectAssetOwnershipMode.Referenced);
            db.Add(unsaved);
            Func<Task> read = async () =>
            {
                var result = await scope.ServiceProvider.GetRequiredService<IObjectAssetContentReader>().OpenReadAsync(uploaded.AssetId, timeout.Token);
                result!.Integrity!.Status.Should().Be(Sha256ValidationStatus.Mismatch);
                await result.Content.DisposeAsync();
            };
            if (behavior == Sha256FailureBehavior.Guard) await read.Should().ThrowAsync<ObjectAssetIntegrityException>();
            else await read();
            db.Entry(unsaved).State.Should().Be(EntityState.Added, "verification must not flush unrelated caller changes");
            using var evidenceScope = fixture.RootProvider.CreateScope();
            var evidenceDb = evidenceScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
            (await evidenceDb.IntegrityEvidence.AsNoTracking().SingleAsync(timeout.Token)).Status.Should().Be(Sha256ValidationStatus.Mismatch);
            var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetPagedReader>();
            (await reader.GetRecordAsync(uploaded.AssetId, timeout.Token))!.Integrity!.Status.Should().Be(Sha256ValidationStatus.Mismatch);
            (await reader.GetTemporaryPageAsync(bindingId, cancellationToken: timeout.Token)).Items.Single().Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
            await transaction.RollbackAsync(timeout.Token);
        }
        using var verify = fixture.RootProvider.CreateScope();
        var after = verify.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await after.TemporaryAssets.AsNoTracking().SingleAsync()).Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
        (await after.IntegrityEvidence.AsNoTracking().SingleAsync()).Resolution.Should().Be("Applied");
    }

    [Test]
    public async Task TransactionalAfterUploadGuard_ShouldKeepEvidenceWhenNewAssetIsRolledBack()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: o =>
        { o.Integrity.FailureBehavior = Sha256FailureBehavior.Guard; o.Integrity.Triggers = Sha256VerificationTriggers.AfterUpload; });
        fixture.StorageProvider.CorruptOnGet = true;
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using (var transaction = await db.Database.BeginTransactionAsync(timeout.Token))
        {
            var session = scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid());
            Func<Task> upload = async () => await session.AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain", cancellationToken: timeout.Token);
            await upload.Should().ThrowAsync<ObjectAssetIntegrityException>();
            await transaction.RollbackAsync(timeout.Token);
        }
        using var verify = fixture.RootProvider.CreateScope();
        var check = verify.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await check.ObjectAssets.CountAsync()).Should().Be(0);
        var evidence = await check.IntegrityEvidence.SingleAsync();
        evidence.Status.Should().Be(Sha256ValidationStatus.Mismatch);
        evidence.Resolution.Should().Be("AssetMissing");
    }

    [Test]
    public async Task TransactionalVerification_Commit_ShouldKeepTrackedRowVersionUsable()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var asset = (await db.FindAssetForUpdateAsync(uploaded.AssetId))!;
            asset.MarkUploadSucceeded(DateTimeOffset.UtcNow, asset.ETag, asset.ProviderVersionId); await db.SaveChangesAsync();
            (await scope.ServiceProvider.GetRequiredService<IObjectAssetIntegrityService>().VerifyAsync(uploaded.AssetId)).Status.Should().Be(Sha256ValidationStatus.Valid);
            await transaction.CommitAsync();
        }
        var tracked = db.ChangeTracker.Entries<ObjectAsset>().Single().Entity;
        tracked.MarkPendingDelete(DateTimeOffset.UtcNow, false);
        await db.SaveChangesAsync();
        using var checkScope = fixture.RootProvider.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var row = await check.TemporaryAssets.AsNoTracking().SingleAsync();
        row.Status.Should().Be(ObjectAssetStatus.PendingDelete);
        row.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Valid);
    }

    [Test]
    public async Task PendingEvidence_ShouldSkipLockedAssetExposeLatestStatusAndRecoverThroughMaintenance()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope();
        var bindingId = Guid.NewGuid();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(bindingId)
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await db.FindAssetAsync(uploaded.AssetId))!;
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>();
        var now = DateTimeOffset.UtcNow;
        await journal.AppendAsync(asset, new(asset.Id, Sha256ValidationStatus.Mismatch, asset.Sha256, new string('b', 64), now, now, asset.ProviderVersionId, "test"), default);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.FindAssetForUpdateAsync(asset.Id);
            (await journal.ApplyAsync()).Resolved.Should().Be(0, "background synchronization must skip a caller-locked identity");
            var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetPagedReader>();
            (await reader.GetRecordAsync(asset.Id))!.Integrity!.Status.Should().Be(Sha256ValidationStatus.Mismatch);
            (await reader.GetTemporaryPageAsync(bindingId)).Items.Single().Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
            await transaction.CommitAsync();
        }
        await fixture.RootProvider.GetRequiredService<IObjectAssetMaintenanceRunner>().RunOnceAsync();
        using var verify = fixture.RootProvider.CreateScope();
        var check = verify.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await check.TemporaryAssets.AsNoTracking().SingleAsync()).Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
        (await check.IntegrityEvidence.SingleAsync()).ResolvedAtUtc.Should().NotBeNull();
    }

    [Test]
    public async Task EvidenceForOlderContent_ShouldNotOverwriteCurrentStatus()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await db.FindAssetAsync(uploaded.AssetId))!;
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>(); var now = DateTimeOffset.UtcNow;
        await journal.AppendAsync(asset, new(asset.Id, Sha256ValidationStatus.Mismatch, asset.Sha256, new string('b', 64), now, now, asset.ProviderVersionId, "old"), default);
        asset.RecordStorageVersion("new", "new-content-version"); await db.SaveChangesAsync();
        await journal.ApplyAsync();
        using var verify = fixture.RootProvider.CreateScope(); var check = verify.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await check.TemporaryAssets.AsNoTracking().SingleAsync()).Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.NotChecked);
        (await check.IntegrityEvidence.SingleAsync()).Resolution.Should().Be("ContentChanged");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AmbientVerification_ShouldSurviveTransactionOutcomeWithoutEnlistingEvidence(bool commit)
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        using (var transaction = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeOption.Required,
            new System.Transactions.TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var asset = (await db.FindAssetAsync(uploaded.AssetId))!;
            asset.MarkUploadSucceeded(DateTimeOffset.UtcNow, asset.ETag, asset.ProviderVersionId); await db.SaveChangesAsync();
            (await scope.ServiceProvider.GetRequiredService<IObjectAssetIntegrityService>().VerifyAsync(uploaded.AssetId)).Status.Should().Be(Sha256ValidationStatus.Valid);
            if (commit) transaction.Complete();
        }
        using var checkScope = fixture.RootProvider.CreateScope(); var check = checkScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await check.IntegrityEvidence.AnyAsync(e => e.ResolvedAtUtc == null, timeout.Token)) await Task.Delay(25, timeout.Token);
        (await check.TemporaryAssets.AsNoTracking().SingleAsync(timeout.Token)).Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Valid);
        if (commit)
        {
            var tracked = db.ChangeTracker.Entries<ObjectAsset>().Single().Entity;
            tracked.MarkPendingDelete(DateTimeOffset.UtcNow, false); await db.SaveChangesAsync();
        }
    }

    [Test]
    public async Task ImplicitTransactionRollback_ShouldSynchronizeDurableEvidenceOnConnectionClose()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var asset = (await db.FindAssetForUpdateAsync(uploaded.AssetId))!;
            asset.MarkUploadSucceeded(DateTimeOffset.UtcNow, asset.ETag, asset.ProviderVersionId); await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IObjectAssetIntegrityService>().VerifyAsync(uploaded.AssetId);
        } // Dispose without commit or an explicit rollback call.
        using var checkScope = fixture.RootProvider.CreateScope(); var check = checkScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await check.TemporaryAssets.AsNoTracking().SingleAsync()).Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Valid);
        (await check.IntegrityEvidence.SingleAsync()).Resolution.Should().Be("Applied");
    }

    [Test]
    public async Task EvidenceRecovery_ShouldRespectNewerResultsAndBoundRetentionWork()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: o => o.Maintenance.BatchSize = 1);
        using var scope = fixture.RootProvider.CreateScope();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await db.FindAssetAsync(uploaded.AssetId))!;
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>(); var now = DateTimeOffset.UtcNow;
        var old = await journal.AppendAsync(asset, new(asset.Id, Sha256ValidationStatus.Mismatch, asset.Sha256, new string('b', 64),
            now.AddDays(-60), now.AddDays(-60), asset.ProviderVersionId, "old"), default);
        var row = await db.IntegrityEvidence.SingleAsync(e => e.Id == old.Id);
        row.ResolvedAtUtc = now.AddDays(-31); row.Resolution = "Applied"; await db.SaveChangesAsync();
        for (var i = 0; i < 2; i++) await journal.AppendAsync(asset, new(asset.Id, Sha256ValidationStatus.Mismatch, asset.Sha256,
            new string('b', 64), now.AddSeconds(-2 + i), now, asset.ProviderVersionId, "old"), default);
        asset.RecordSha256Validation(Sha256ValidationStatus.Valid, now, now, asset.Sha256, asset.ProviderVersionId, null);
        await db.SaveChangesAsync();
        (await journal.MaintainAsync(default)).Should().Be(2, "one pending row and one expired resolved row form the bounded work");
        using var checkScope = fixture.RootProvider.CreateScope(); var check = checkScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await check.IntegrityEvidence.CountAsync(e => e.ResolvedAtUtc == null)).Should().Be(1);
        (await check.IntegrityEvidence.CountAsync(e => e.Resolution == "Superseded")).Should().Be(1);
        (await check.IntegrityEvidence.CountAsync(e => e.Id == old.Id)).Should().Be(0);
        (await check.TemporaryAssets.AsNoTracking().SingleAsync()).Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Valid);
    }

    [TestCase(System.Data.IsolationLevel.Serializable)]
    [TestCase(System.Data.IsolationLevel.RepeatableRead)]
    [TestCase(System.Data.IsolationLevel.Snapshot)]
    public async Task EvidenceReads_ShouldNotPreventLaterEvidenceWritesInStricterTransactions(System.Data.IsolationLevel isolation)
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope();
        var bindingId = Guid.NewGuid();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(bindingId)
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        if (isolation == System.Data.IsolationLevel.Snapshot)
            await db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;");
        var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetPagedReader>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var transaction = await db.Database.BeginTransactionAsync(isolation, timeout.Token);
        await reader.GetRecordAsync(uploaded.AssetId, timeout.Token);
        await reader.GetTemporaryPageAsync(bindingId, cancellationToken: timeout.Token);
        var asset = (await db.FindAssetForUpdateAsync(uploaded.AssetId, timeout.Token))!;
        asset.MarkUploadSucceeded(DateTimeOffset.UtcNow, asset.ETag, asset.ProviderVersionId); await db.SaveChangesAsync(timeout.Token);
        (await scope.ServiceProvider.GetRequiredService<IObjectAssetIntegrityService>().VerifyAsync(uploaded.AssetId, timeout.Token))
            .Status.Should().Be(Sha256ValidationStatus.Valid);
        (await reader.GetRecordAsync(uploaded.AssetId, timeout.Token))!.Integrity!.Status.Should().Be(Sha256ValidationStatus.Valid);
        (await reader.GetTemporaryPageAsync(bindingId, cancellationToken: timeout.Token)).Items.Single().Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Valid);
        await transaction.RollbackAsync(timeout.Token);
    }

    [Test]
    public async Task EvidenceFoldedBetweenMetadataAndEvidenceReads_ShouldStillBeVisible()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var snapshot = (await db.FindAssetAsync(uploaded.AssetId, false))!;
        snapshot.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.NotChecked);
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>(); var now = DateTimeOffset.UtcNow;
        await journal.AppendAsync(snapshot, new(snapshot.Id, Sha256ValidationStatus.Mismatch, snapshot.Sha256,
            new string('b', 64), now, now, snapshot.ProviderVersionId, "race"), default);
        await journal.ApplyAsync(); // Complete synchronization after the metadata snapshot was read.
        (await journal.LatestAsync(snapshot, db, default))!.Status.Should().Be(Sha256ValidationStatus.Mismatch);
    }

    private sealed class AuditSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private bool _released;
        private int _posts;
        public int PostCount => _posts;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _posts); _queue.Enqueue((callback, state)); if (_released) Drain();
        }
        public void Release() { _released = true; Drain(); }
        private void Drain() { while (_queue.TryDequeue(out var item)) { var copy = item; ThreadPool.QueueUserWorkItem(_ => copy.Callback(copy.State)); } }
    }
}
