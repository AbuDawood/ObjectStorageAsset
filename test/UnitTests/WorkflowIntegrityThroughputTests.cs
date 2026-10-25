using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed partial class WorkflowTests
{
    [Test]
    public async Task EvidenceBackoff_ShouldLetNewerRowsProgressAndSurviveWorkerRestart()
    {
        var clock = new EvidenceClock();
        await using var fixture = await SqlServerFixture.CreateAsync(timeProvider: clock, configure: o =>
        { o.Maintenance.Evidence.BatchSize = 2; o.Maintenance.Evidence.MaximumBatchesPerRun = 4; });
        using var scope = fixture.RootProvider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var assets = await SeedEvidenceAssetsAsync(db, 6);
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>();
        foreach (var asset in assets) await journal.AppendAsync(asset, EvidenceResult(asset, clock.Now), default);
        await using (var blocker = LockContext(fixture))
        await using (var transaction = await blocker.Database.BeginTransactionAsync())
        {
            await blocker.ReserveAssetsForUpdateAsync(assets.Take(2).Select(a => a.Id));
            (await journal.MaintainAsync(default)).Should().Be(4);
            var pending = await db.IntegrityEvidence.AsNoTracking().Where(e => e.ResolvedAtUtc == null).ToArrayAsync();
            pending.Should().HaveCount(2); pending.Should().OnlyContain(e => e.RetryCount == 1 && e.NextAttemptAtUtc > clock.Now);
            await journal.MaintainAsync(default);
            (await db.IntegrityEvidence.AsNoTracking().Where(e => e.ResolvedAtUtc == null).Select(e => e.RetryCount).ToArrayAsync())
                .Should().OnlyContain(n => n == 1, "new failures must not be retried in the same cycle or before their persisted due time");
            clock.Now = clock.Now.AddSeconds(2);
            await journal.MaintainAsync(default);
            (await db.IntegrityEvidence.AsNoTracking().Where(e => e.ResolvedAtUtc == null).Select(e => e.RetryCount).ToArrayAsync()).Should().OnlyContain(n => n == 2);
            await transaction.RollbackAsync();
        }
        clock.Now = clock.Now.AddSeconds(3);
        var persistence = fixture.RootProvider.GetRequiredService<ObjectStorageAssetPersistenceOptions>();
        var restarted = new ObjectAssetIntegrityJournal(persistence, new("osa"),
            fixture.RootProvider.GetRequiredService<ObjectStorageAssetRuntimeOptions>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ObjectAssetIntegrityJournal>.Instance, clock);
        (await restarted.MaintainAsync(default)).Should().Be(2);
        (await db.IntegrityEvidence.CountAsync(e => e.ResolvedAtUtc == null)).Should().Be(0);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EvidenceBatch_ShouldCoalesceAssetWritesAndKeepEveryAuditResult(bool synchronous)
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await SeedEvidenceAssetsAsync(db, 1)).Single();
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>(); var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 20; i++)
            await journal.AppendAsync(asset, EvidenceResult(asset, now.AddMilliseconds(i), i == 19 ? Sha256ValidationStatus.Mismatch : Sha256ValidationStatus.Valid), default);
        var batch = synchronous ? journal.Apply() : await journal.ApplyAsync();
        batch.Resolved.Should().Be(20); batch.Mirrors.Should().ContainSingle();
        (await db.IntegrityEvidence.CountAsync()).Should().Be(20);
        (await db.IntegrityEvidence.CountAsync(e => e.Resolution == "Applied")).Should().Be(1);
        (await db.IntegrityEvidence.CountAsync(e => e.Resolution == "Superseded")).Should().Be(19);
        (await db.FindAssetAsync(asset.Id, false))!.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
    }

    [Test]
    public async Task EvidencePurge_ShouldUseIndependentCapacityAndExposeBacklogMetrics()
    {
        var clock = new EvidenceClock();
        await using var fixture = await SqlServerFixture.CreateAsync(timeProvider: clock, configure: o =>
        {
            o.Integrity.EvidenceRetention = TimeSpan.FromDays(7); o.Maintenance.Evidence.BatchSize = 1;
            o.Maintenance.Evidence.PurgeBatchSize = 50; o.Maintenance.Evidence.MaximumBatchesPerRun = 2;
        });
        using var scope = fixture.RootProvider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await SeedEvidenceAssetsAsync(db, 1)).Single();
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>();
        for (var i = 0; i < 3; i++) await journal.AppendAsync(asset, EvidenceResult(asset, clock.Now.AddTicks(i)), default);
        var expired = Enumerable.Range(0, 120).Select(_ => new ObjectAssetIntegrityEvidence
        { AssetId = Guid.NewGuid(), ExpectedHash = asset.Sha256, AttemptedAtUtc = clock.Now.AddDays(-9),
            ResolvedAtUtc = clock.Now.AddDays(-8), Resolution = "AssetMissing" });
        db.IntegrityEvidence.AddRange(expired);
        var retained = new ObjectAssetIntegrityEvidence { AssetId = asset.Id, ExpectedHash = asset.Sha256,
            AttemptedAtUtc = clock.Now.AddDays(-100), NextAttemptAtUtc = clock.Now.AddHours(1) };
        db.IntegrityEvidence.Add(retained); await db.SaveChangesAsync();
        (await journal.MaintainAsync(default)).Should().Be(102, "two applies and two independently sized purge batches are permitted");
        var snapshot = await journal.ObserveAsync(default);
        snapshot.Pending.Should().Be(2); snapshot.Expired.Should().Be(20);
        snapshot.OldestPendingAtUtc.Should().Be(retained.AttemptedAtUtc);
        (await db.IntegrityEvidence.AsNoTracking().SingleAsync(e => e.Id == retained.Id)).ResolvedAtUtc.Should().BeNull();
        var gauges = new Dictionary<string, double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == ObjectAssetIntegrityMetrics.MeterName) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => gauges[instrument.Name] = value);
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => gauges[instrument.Name] = value);
        listener.Start();
        ObjectAssetIntegrityMetrics.Publish(new() { ObservedAtUtc = snapshot.ObservedAtUtc, PendingEvidenceCount = snapshot.Pending,
            OldestPendingEvidenceAtUtc = snapshot.OldestPendingAtUtc, ExpiredEvidenceCount = snapshot.Expired });
        listener.RecordObservableInstruments();
        gauges["osa.integrity.evidence.pending"].Should().Be(2);
        gauges["osa.integrity.evidence.expired"].Should().Be(20);
        gauges["osa.integrity.evidence.oldest_pending_age"].Should().BeGreaterThan(90 * 86400);
    }

    [Test]
    public async Task JournalConnections_ShouldReusePhysicalConnectionAndResetLockTimeout()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await SeedEvidenceAssetsAsync(db, 1)).Single();
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>();
        var connections = new ConcurrentBag<Guid>();
        using var diagnostics = new ConnectionObservations(fixture.DatabaseName, connections);
        await journal.AppendAsync(asset, EvidenceResult(asset, DateTimeOffset.UtcNow), default);
        await journal.AppendAsync(asset, EvidenceResult(asset, DateTimeOffset.UtcNow), default);
        connections.Count.Should().Be(2); connections.Distinct().Should().ContainSingle("journal inserts must reuse their SQL physical connection");
        await journal.ApplyAsync();
        var connectionString = new SqlConnectionStringBuilder(fixture.ConnectionString) { ConnectTimeout = 5, Enlist = false, Pooling = true };
        await using var connection = new SqlConnection(connectionString.ConnectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT @@LOCK_TIMEOUT;";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(-1, "zero lock timeout must not leak to the next pool borrower");
        connections.Distinct().Should().ContainSingle();
    }

    [Test]
    public async Task IdenticalPods_ShouldShareSqlScheduleBackoffAndObservationSnapshot()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(); var clock = new EvidenceClock();
        var firstTasks = new PodTasks { FailExpiry = true }; var secondTasks = new PodTasks();
        ServiceProvider Pod(PodTasks tasks)
        {
            var services = new ServiceCollection().AddObjectStorageAsset(o =>
            { o.ConnectionString = fixture.ConnectionString; o.DefaultStorageNamespace = "test"; o.EnableMaintenanceWorker = true; });
            services.AddSingleton<TimeProvider>(clock); services.AddScoped<IObjectAssetMaintenanceTasks>(_ => tasks);
            return services.BuildServiceProvider(validateScopes: true);
        }
        using var first = Pod(firstTasks); using var second = Pod(secondTasks);
        await first.GetRequiredService<IObjectAssetMaintenanceRunner>().RunOnceAsync();
        clock.Now = clock.Now.AddSeconds(10);
        await second.GetRequiredService<IObjectAssetMaintenanceRunner>().RunOnceAsync();
        secondTasks.Calls.Should().BeEmpty("a second pod must respect the first pod's shared due times and failure backoff");
        clock.Now = clock.Now.AddMinutes(2);
        await second.GetRequiredService<IObjectAssetMaintenanceRunner>().RunOnceAsync();
        secondTasks.Calls.Should().Equal("expiry");
        using var checkScope = fixture.RootProvider.CreateScope(); var db = checkScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var states = await db.MaintenanceState.AsNoTracking().ToDictionaryAsync(s => s.Name);
        states.Should().HaveCount(5); states["expiry"].FailureCount.Should().Be(0);
        states["expiry"].LastSucceededAtUtc.Should().Be(clock.Now);
        states["integrity-observation"].PendingEvidenceCount.Should().Be(3);
    }

    private static ObjectAssetIntegrityResult EvidenceResult(ObjectAsset asset, DateTimeOffset attempted,
        Sha256ValidationStatus status = Sha256ValidationStatus.Valid) => new(asset.Id, status, asset.Sha256,
            status == Sha256ValidationStatus.Valid ? asset.Sha256 : new string('b', 64), attempted, attempted, asset.ProviderVersionId, null);

    private static async Task<ObjectAsset[]> SeedEvidenceAssetsAsync(ObjectStorageAssetDbContext db, int count)
    {
        var assets = Enumerable.Range(0, count).Select(_ => ObjectAsset.CreatePendingUpload(Guid.NewGuid(), "order", "files",
            ObjectAssetSlotMultiplicity.Many, "osa-dev", "test", "evidence/" + Guid.NewGuid(), "a.txt", ".txt", "text/plain",
            DateTimeOffset.UtcNow, 4, new string('a', 64))).ToArray();
        foreach (var asset in assets) { asset.BindOwner(42L); asset.MarkUploadSucceeded(DateTimeOffset.UtcNow, null, null); }
        db.AddRange(assets); await db.SaveChangesAsync(); return assets;
    }

    private sealed class EvidenceClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class PodTasks : IObjectAssetMaintenanceTasks
    {
        public bool FailExpiry { get; init; }
        public List<string> Calls { get; } = [];
        public Task<int> ExpireCoreAsync(DateTimeOffset? now, CancellationToken ct)
        { Calls.Add("expiry"); return FailExpiry ? Task.FromException<int>(new IOException("Injected")) : Task.FromResult(0); }
        public Task<int> PendingCoreAsync(CancellationToken ct) { Calls.Add("pending"); return Task.FromResult(0); }
        public Task<int> RetryCoreAsync(CancellationToken ct, DateTimeOffset? failedBefore = null) { Calls.Add("retry"); return Task.FromResult(0); }
        public Task<int> SynchronizeIntegrityEvidenceAsync(CancellationToken ct) { Calls.Add("integrity"); return Task.FromResult(0); }
        public Task<IntegrityBacklogSnapshot> ObserveIntegrityEvidenceAsync(CancellationToken ct)
        { Calls.Add("observation"); return Task.FromResult(new IntegrityBacklogSnapshot(DateTimeOffset.UtcNow, 3, DateTimeOffset.UtcNow, 2)); }
    }

    private sealed class ConnectionObservations : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string _database; private readonly ConcurrentBag<Guid> _connections;
        private readonly IDisposable _all; private readonly List<IDisposable> _subscriptions = [];
        public ConnectionObservations(string database, ConcurrentBag<Guid> connections)
        { _database = database; _connections = connections; _all = DiagnosticListener.AllListeners.Subscribe(this); }
        public void OnNext(DiagnosticListener listener)
        { if (listener.Name == "SqlClientDiagnosticListener") _subscriptions.Add(listener.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != "Microsoft.Data.SqlClient.WriteConnectionOpenAfter") return;
            var connection = value.Value?.GetType().GetProperty("Connection")?.GetValue(value.Value) as SqlConnection;
            if (connection?.Database == _database && connection.ConnectionTimeout == 5) _connections.Add(connection.ClientConnectionId);
        }
        public void OnCompleted() { } public void OnError(Exception error) { }
        public void Dispose() { _all.Dispose(); foreach (var subscription in _subscriptions) subscription.Dispose(); }
    }
}
