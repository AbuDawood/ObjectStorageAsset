using System.Data.Common;
using System.Data.SqlTypes;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed partial class WorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MutationLookup_ShouldSerializeCompetingMovesBeforeReadingPayload(bool explicitLookup)
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var id = await SeedLockAssetAsync(fixture);
        var started = new IdentityReadStarted();
        await using var first = LockContext(fixture);
        await using var second = LockContext(fixture, started);
        await using var firstTx = await first.Database.BeginTransactionAsync();
        await using var secondTx = await second.Database.BeginTransactionAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var asset = explicitLookup ? await first.FindAssetForUpdateAsync(id, timeout.Token) : await first.FindAssetAsync(id, true, timeout.Token);
        var contender = explicitLookup ? second.FindAssetForUpdateAsync(id, timeout.Token) : second.FindAssetAsync(id, true, timeout.Token);
        await started.Started.Task.WaitAsync(timeout.Token);
        await Task.Delay(100, timeout.Token);
        contender.IsCompleted.Should().BeFalse("the second writer must wait before reading the old payload");
        asset!.MarkDeleted(DateTimeOffset.UtcNow, false);
        await first.SaveChangesAsync(timeout.Token);
        await firstTx.CommitAsync(timeout.Token);
        var observed = await contender.WaitAsync(timeout.Token);
        observed.Should().BeOfType<ArchivedObjectAsset>();
        observed!.Status.Should().Be(ObjectAssetStatus.Deleted);
        await secondTx.CommitAsync(timeout.Token);
    }

    [Test]
    public async Task PendingWrite_ShouldReserveIdentityBeforePayloadAndNotDeadlockWithReader()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var id = await SeedLockAssetAsync(fixture);
        var started = new IdentityReadStarted();
        await using var writer = LockContext(fixture);
        await using var reader = LockContext(fixture, started);
        await using var transaction = await writer.Database.BeginTransactionAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var asset = (await writer.FindAssetForUpdateAsync(id, timeout.Token))!;
        asset.MarkPendingDelete(DateTimeOffset.UtcNow, false);
        await writer.SaveChangesAsync(timeout.Token);
        var reading = reader.FindAssetAsync(id, false, timeout.Token);
        await started.Started.Task.WaitAsync(timeout.Token);
        await Task.Delay(100, timeout.Token);
        reading.IsCompleted.Should().BeFalse();
        asset.MarkDeleted(DateTimeOffset.UtcNow, false);
        await writer.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        (await reading.WaitAsync(timeout.Token)).Should().BeOfType<ArchivedObjectAsset>();
    }

    [Test]
    public async Task ReversedMultiAssetMoves_ShouldUseOneLockOrderAndPreserveConservation()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var ids = new[] { await SeedLockAssetAsync(fixture), await SeedLockAssetAsync(fixture) };
        await using var a = LockContext(fixture);
        await using var b = LockContext(fixture);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var rows = await a.PermanentAssets.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(timeout.Token);
        foreach (var row in rows) { a.Attach(row); row.MarkDeleted(DateTimeOffset.UtcNow, false); }
        var reversed = await b.PermanentAssets.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(timeout.Token);
        foreach (var row in reversed.Reverse()) { b.Attach(row); row.MarkDeleted(DateTimeOffset.UtcNow, false); }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Move(ObjectStorageAssetDbContext db)
        {
            await start.Task;
            try { await db.SaveChangesAsync(timeout.Token); return "committed"; }
            catch (DbUpdateConcurrencyException) { return "stale"; }
        }
        var first = Move(a); var second = Move(b); start.SetResult();
        (await Task.WhenAll(first, second).WaitAsync(timeout.Token)).Should().BeEquivalentTo("committed", "stale");
        await using var check = LockContext(fixture);
        (await check.AssetIdentities.CountAsync(timeout.Token)).Should().Be(2);
        (await check.ArchivedAssets.CountAsync(timeout.Token)).Should().Be(2);
        (await check.PermanentAssets.CountAsync(timeout.Token)).Should().Be(0);
    }

    [Test]
    public async Task IndependentAssetsAndReadOnlyLookups_ShouldRemainConcurrent()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var a = await SeedLockAssetAsync(fixture); var b = await SeedLockAssetAsync(fixture);
        await using var writer = LockContext(fixture);
        await using var other = LockContext(fixture);
        await using var reader = LockContext(fixture);
        await using var transaction = await writer.Database.BeginTransactionAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await writer.FindAssetForUpdateAsync(a, timeout.Token);
        (await other.FindAssetAsync(b, true, timeout.Token)).Should().NotBeNull();
        (await reader.FindAssetAsync(a, false, timeout.Token)).Should().NotBeNull("an update reservation permits pure readers before payload writes");
    }

    [Test]
    public async Task OrderedReservation_ShouldAcceptReversedInputAndKeepMovesAtomic()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var ids = new[] { await SeedLockAssetAsync(fixture), await SeedLockAssetAsync(fixture) };
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        async Task Move(IEnumerable<Guid> order)
        {
            await using var db = LockContext(fixture);
            await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
            await start.Task;
            await db.ReserveAssetsForUpdateAsync(order, timeout.Token);
            foreach (var id in order)
            {
                var asset = (await db.FindAssetForUpdateAsync(id, timeout.Token))!;
                if (asset.Status != ObjectAssetStatus.Deleted) asset.MarkDeleted(DateTimeOffset.UtcNow, false);
            }
            await db.SaveChangesAsync(timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }
        var a = Move(ids); var b = Move(ids.Reverse()); start.SetResult();
        await Task.WhenAll(a, b).WaitAsync(timeout.Token);
        await using var check = LockContext(fixture);
        (await check.ArchivedAssets.CountAsync(timeout.Token)).Should().Be(2);
        (await check.AssetIdentities.CountAsync(timeout.Token)).Should().Be(2);
    }

    private static ObjectStorageAssetDbContext LockContext(SqlServerFixture fixture, IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ObjectStorageAssetDbContext>().UseSqlServer(fixture.ConnectionString, sql => sql.CommandTimeout(15));
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options, new ObjectStorageAssetSchema("osa"));
    }

    [Test]
    public async Task Coordinator_ShouldReserveReversedRemovalCommandsBeforeCallerTransactionWrites()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(physicallyDeleteRequestedAssets: false);
        var ids = new[] { await SeedLockAssetAsync(fixture), await SeedLockAssetAsync(fixture) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Remove(IEnumerable<Guid> order)
        {
            using var scope = fixture.RootProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
            await using var host = fixture.CreateHostDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
            var session = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>().For(new Order { Id = 42 });
            foreach (var id in order) await session.RemoveAsync(id, timeout.Token);
            await start.Task;
            await scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>().SaveChangesWithAssetsAsync(host, timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }
        var a = Remove(ids); var b = Remove(ids.Reverse()); start.SetResult();
        await Task.WhenAll(a, b).WaitAsync(timeout.Token);
        await using var check = LockContext(fixture);
        (await check.ArchivedAssets.CountAsync(timeout.Token)).Should().Be(2);
        fixture.StorageProvider.ObjectKeys.Should().BeEmpty();
    }

    private static async Task<Guid> SeedLockAssetAsync(SqlServerFixture fixture)
    {
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = ObjectAsset.CreatePendingUpload(Guid.NewGuid(), "order", "attachments", ObjectAssetSlotMultiplicity.Many,
            "osa-dev", "unit-tests", "locks/" + Guid.NewGuid(), "a.txt", ".txt", "text/plain", DateTimeOffset.UtcNow,
            4, new string('a', 64));
        asset.BindOwner(42L); asset.MarkUploadSucceeded(DateTimeOffset.UtcNow, "test", null);
        db.Add(asset); await db.SaveChangesAsync(); return asset.Id;
    }

    [Test]
    public async Task LockAcquisitionDeadlock_ShouldRetryBeforeAnyPayloadWrite()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var ids = new[] { await SeedLockAssetAsync(fixture), await SeedLockAssetAsync(fixture) }
            .OrderBy(id => new SqlGuid(id)).ToArray();
        var attempts = new MutationLockAttempts();
        await using var saver = LockContext(fixture, attempts);
        await using var blocker = LockContext(fixture);
        await using var observer = LockContext(fixture);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var rows = await saver.PermanentAssets.ToArrayAsync(timeout.Token);
        foreach (var row in rows) row.MarkDeleted(DateTimeOffset.UtcNow, false);
        await saver.Database.OpenConnectionAsync(timeout.Token);
        await using var session = saver.Database.GetDbConnection().CreateCommand();
        session.CommandText = "SET DEADLOCK_PRIORITY LOW; SELECT @@SPID;";
        var sessionId = Convert.ToInt32(await session.ExecuteScalarAsync(timeout.Token));
        await using var transaction = await blocker.Database.BeginTransactionAsync(timeout.Token);
        const string lockSql = "DECLARE @held uniqueidentifier; SELECT @held=[AssetId] FROM [osa].[ObjectAssetIdentity] WITH (XLOCK,HOLDLOCK) WHERE [AssetId]=@id;";
        await blocker.Database.ExecuteSqlRawAsync(lockSql, [new SqlParameter("@id", ids[1])], timeout.Token);
        var saving = saver.SaveChangesAsync(timeout.Token);
        while (await observer.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.dm_exec_requests WHERE session_id=@session AND wait_type LIKE 'LCK_M_%'",
            new SqlParameter("@session", sessionId)).SingleAsync(timeout.Token) == 0)
        {
            if (saving.IsCompleted) await saving;
            await Task.Delay(25, timeout.Token);
        }
        // Saver holds the first identity and waits for the second; the deliberately reversed external
        // locker now closes the cycle. LOW priority makes the pre-write saver the deadlock victim.
        await blocker.Database.ExecuteSqlRawAsync(lockSql, [new SqlParameter("@id", ids[0])], timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        await saving.WaitAsync(timeout.Token);
        attempts.Count.Should().BeGreaterThanOrEqualTo(2);
        (await observer.ArchivedAssets.CountAsync(timeout.Token)).Should().Be(2);
        (await observer.AssetIdentities.CountAsync(timeout.Token)).Should().Be(2);
        (await observer.PermanentAssets.CountAsync(timeout.Token)).Should().Be(0);
    }

    private sealed class MutationLockAttempts : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DECLARE osa_asset_locks", StringComparison.Ordinal)) Count++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class IdentityReadStarted : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ObjectAssetIdentity", StringComparison.Ordinal)) Started.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
