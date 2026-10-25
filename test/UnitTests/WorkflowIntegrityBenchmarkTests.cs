using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed partial class WorkflowTests
{
    [Test, Category("Scale")]
    public async Task SustainedEvidence_ShouldDrainAlongsideConcurrentVerification()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("OSA_EVIDENCE_BENCHMARK_OPERATIONS"), out var operations))
            Assert.Ignore("Set OSA_EVIDENCE_BENCHMARK_OPERATIONS (for example 2000) to run the disposable evidence throughput benchmark.");
        if (operations is < 100 or > 50000) throw new ArgumentOutOfRangeException(nameof(operations));
        var backlog = int.TryParse(Environment.GetEnvironmentVariable("OSA_EVIDENCE_BENCHMARK_BACKLOG"), out var configured) ? configured : 100000;
        if (backlog is < 1000 or > 1000000) throw new ArgumentOutOfRangeException(nameof(backlog));
        const int concurrency = 4;
        await using var fixture = await SqlServerFixture.CreateAsync(configure: o =>
        {
            o.Integrity.EvidenceRetention = TimeSpan.FromDays(7);
            o.Maintenance.IntegrityEvidenceInterval = TimeSpan.FromMilliseconds(250);
            o.Maintenance.Evidence.BatchSize = 200; o.Maintenance.Evidence.PurgeBatchSize = 1000;
            o.Maintenance.Evidence.MaximumBatchesPerRun = 10; o.Maintenance.Evidence.TimeBudget = TimeSpan.FromMilliseconds(500);
            o.Maintenance.Evidence.MetricsInterval = TimeSpan.FromSeconds(1);
        });
        using var seedScope = fixture.RootProvider.CreateScope();
        var db = seedScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var session = seedScope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid());
        var ids = new List<Guid>();
        for (var i = 0; i < concurrency; i++)
            ids.Add((await session.AddAsync(OrderAssets.Attachments, CreateStream(new string('a', 1024)), "bench.txt", "text/plain")).AssetId);
        var assets = await db.TemporaryAssets.AsNoTracking().Where(a => ids.Contains(a.Id)).Cast<ObjectAsset>().ToArrayAsync();
        var recoveryAssets = await SeedEvidenceAssetsAsync(db, 1000);
        await BulkSeedEvidenceAsync(db, assets, backlog, resolved: true);
        await BulkSeedEvidenceAsync(db, recoveryAssets, 5000, resolved: false);
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>();
        var before = await journal.ObserveAsync(default);
        before.Pending.Should().Be(5000); before.Expired.Should().Be(backlog);

        var connections = new ConcurrentBag<Guid>(); using var diagnostics = new ConnectionObservations(fixture.DatabaseName, connections);
        var latencies = new ConcurrentBag<double>(); var completed = 0; var cycles = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var runner = fixture.RootProvider.GetRequiredService<IObjectAssetMaintenanceRunner>();
        async Task Maintain()
        {
            while (!stop.IsCancellationRequested)
            {
                await runner.RunOnceAsync(stop.Token); Interlocked.Increment(ref cycles);
                await Task.Delay(50, stop.Token);
            }
        }
        var timer = Stopwatch.StartNew(); var maintenance = Maintain();
        IntegrityBacklogSnapshot? after = null;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
            {
                for (var i = worker; i < operations; i += concurrency)
                {
                    var start = Stopwatch.GetTimestamp();
                    using var scope = fixture.RootProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
                    await using var transaction = await context.Database.BeginTransactionAsync(timeout.Token);
                    await context.FindAssetForUpdateAsync(ids[worker], timeout.Token);
                    var result = await scope.ServiceProvider.GetRequiredService<IObjectAssetIntegrityService>().VerifyAsync(ids[worker], timeout.Token);
                    result.Status.Should().Be(Sha256ValidationStatus.Valid);
                    await transaction.CommitAsync(timeout.Token);
                    latencies.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); Interlocked.Increment(ref completed);
                }
            }));
            var producerSeconds = timer.Elapsed.TotalSeconds;
            do
            {
                after = await journal.ObserveAsync(timeout.Token);
                if (after.Pending == 0 && after.Expired == 0) break;
                await Task.Delay(100, timeout.Token);
            } while (!timeout.IsCancellationRequested);
            after!.Pending.Should().Be(0); after.Expired.Should().Be(0);
            var ordered = latencies.Order().ToArray();
            TestContext.Progress.WriteLine($"Evidence throughput: {completed} transactional verifications, {concurrency} producers, {backlog:N0} expired + 5,000 pending starting rows across 1,000 recovery assets.");
            TestContext.Progress.WriteLine($"Producers: {producerSeconds:F2}s, {completed / producerSeconds:F1} verifications/s; transaction p50={ordered[ordered.Length / 2]:F2}ms, p95={ordered[(int)(ordered.Length * 0.95)]:F2}ms.");
            TestContext.Progress.WriteLine($"Total with backlog drained: {timer.Elapsed.TotalSeconds:F2}s, {cycles} scheduler polls; journal opens={connections.Count}, physical connections={connections.Distinct().Count()}; pending={after.Pending}, expired={after.Expired}.");
            connections.Distinct().Count().Should().BeLessThan(operations / 4, "journal work must reuse pooled physical connections");
            (await db.IntegrityEvidence.CountAsync()).Should().Be(5000 + operations, "only expired resolved rows are pruned");
        }
        finally
        {
            stop.Cancel();
            try { await maintenance; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    private static async Task BulkSeedEvidenceAsync(ObjectStorageAssetDbContext db, ObjectAsset[] assets, int count, bool resolved)
    {
        var entity = db.Model.FindEntityType(typeof(ObjectAssetIntegrityEvidence))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var properties = entity.GetProperties().Where(p => p.PropertyInfo is not null && p.GetComputedColumnSql() is null
            && p.Name != nameof(ObjectAssetIntegrityEvidence.Id)).ToArray();
        using var rows = new DataTable();
        foreach (var property in properties)
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            rows.Columns.Add(property.GetColumnName(store)!, type.IsEnum ? Enum.GetUnderlyingType(type) : type);
        }
        await using var connection = new SqlConnection(db.Database.GetConnectionString()); await connection.OpenAsync();
        using var bulk = new SqlBulkCopy(connection) { DestinationTableName = $"[{entity.GetSchema()}].[{entity.GetTableName()}]", BulkCopyTimeout = 120 };
        foreach (DataColumn column in rows.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            var asset = assets[i % assets.Length];
            var evidence = new ObjectAssetIntegrityEvidence
            {
                AssetId = asset.Id, StorageAuthorityId = asset.StorageAuthorityId, BucketName = asset.BucketName,
                ObjectKey = asset.ObjectKey, ExpectedHash = asset.Sha256, ExpectedSize = asset.SizeBytes,
                RequestedVersion = asset.ProviderVersionId, ActualHash = asset.Sha256, Status = Sha256ValidationStatus.Valid,
                AttemptedAtUtc = now.AddDays(-9).AddTicks(i), CompletedAtUtc = now.AddDays(-9).AddTicks(i),
                NextAttemptAtUtc = now, ResolvedAtUtc = resolved ? now.AddDays(-8) : null, Resolution = resolved ? "Applied" : null
            };
            rows.Rows.Add(properties.Select(p =>
            {
                var value = p.PropertyInfo!.GetValue(evidence);
                return value is null ? DBNull.Value : value is Enum ? Convert.ToInt32(value) : value;
            }).ToArray());
            if (rows.Rows.Count == 5000 || i == count - 1) { await bulk.WriteToServerAsync(rows); rows.Clear(); }
        }
    }
}
