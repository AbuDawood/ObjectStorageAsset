using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed partial class WorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task EvidenceRetry_WithBusyAsset_ShouldPersistThroughBothBulkUpdateApis(bool synchronous)
    {
        var clock = new EvidenceClock();
        await using var fixture = await SqlServerFixture.CreateAsync(timeProvider: clock);
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await SeedEvidenceAssetsAsync(db, 1)).Single();
        var journal = fixture.RootProvider.GetRequiredService<ObjectAssetIntegrityJournal>();
        var evidence = await journal.AppendAsync(asset, EvidenceResult(asset, clock.Now), default);
        await using (var blocker = LockContext(fixture))
        await using (var transaction = await blocker.Database.BeginTransactionAsync())
        {
            await blocker.ReserveAssetsForUpdateAsync([asset.Id]);
            var deferred = synchronous ? journal.Apply([evidence.Id]) : await journal.ApplyAsync([evidence.Id]);
            deferred.Resolved.Should().Be(0);
            var retry = await db.IntegrityEvidence.AsNoTracking().SingleAsync();
            retry.RetryCount.Should().Be(1);
            retry.SynchronizationError.Should().Be("Busy");
            retry.NextAttemptAtUtc.Should().BeAfter(clock.Now);
            retry.ResolvedAtUtc.Should().BeNull();
            await transaction.RollbackAsync();
        }
        var recovered = synchronous ? journal.Apply([evidence.Id]) : await journal.ApplyAsync([evidence.Id]);
        recovered.Resolved.Should().Be(1);
        (await db.IntegrityEvidence.AsNoTracking().SingleAsync()).Resolution.Should().Be("Applied");
        (await db.FindAssetAsync(asset.Id, false))!.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Valid);
    }
}
