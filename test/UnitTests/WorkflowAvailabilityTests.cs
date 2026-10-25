using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed partial class WorkflowTests
{
    [TestCase(false, Sha256FailureBehavior.Warning)]
    [TestCase(true, Sha256FailureBehavior.Warning)]
    [TestCase(true, Sha256FailureBehavior.Guard)]
    public async Task MissingContent_ShouldPersistIndependentlyAndShortCircuitRepeatedReads(bool onRead, Sha256FailureBehavior mode)
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: o =>
        { o.Integrity.Triggers = onRead ? Sha256VerificationTriggers.OnRead : Sha256VerificationTriggers.ExplicitOnly; o.Integrity.FailureBehavior = mode; });
        using var scope = fixture.RootProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var uploaded = await sp.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("missing"), "m.txt", "text/plain");
        var db = sp.GetRequiredService<ObjectStorageAssetDbContext>();
        var row = (await db.FindAssetAsync(uploaded.AssetId))!;
        fixture.StorageProvider.RemoveObject(row.ObjectKey);
        var reader = sp.GetRequiredService<IObjectAssetContentReader>();
        var before = fixture.StorageProvider.GetCount;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using (var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, timeout.Token))
        {
            row = (await db.FindAssetForUpdateAsync(uploaded.AssetId, timeout.Token))!;
            row.MarkUploadSucceeded(DateTimeOffset.UtcNow, row.ETag, row.ProviderVersionId);
            await db.SaveChangesAsync(timeout.Token); // Real uncommitted payload and identity locks.
            (await sp.GetRequiredService<IObjectAssetPagedReader>().GetTemporaryPageAsync(row.TemporaryBindingId!.Value,
                cancellationToken: timeout.Token)).Items.Single().ContentAvailability.Should().Be(ObjectAssetContentAvailability.Unknown);
            Func<Task> read = async () => { await reader.OpenReadAsync(uploaded.AssetId, timeout.Token); };
            (await read.Should().ThrowAsync<ObjectAssetContentMissingException>()).Which.Result.MissingReason.Should().Be(ObjectAssetMissingReason.ObjectNotFound);
            // Read metadata inside SERIALIZABLE too: it must not pin the independently written state.
            var record = await sp.GetRequiredService<IObjectAssetPagedReader>().GetRecordAsync(uploaded.AssetId, timeout.Token);
            record!.Asset.ContentAvailability.Should().Be(ObjectAssetContentAvailability.Missing);
            for (var i = 0; i < 3; i++) await read.Should().ThrowAsync<ObjectAssetContentMissingException>();
            await transaction.RollbackAsync(timeout.Token);
        }
        fixture.StorageProvider.GetCount.Should().Be(before + 1);
        using var checkScope = fixture.RootProvider.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var state = await check.ContentStates.SingleAsync();
        state.Availability.Should().Be(ObjectAssetContentAvailability.Missing);
        (await check.FindAssetAsync(uploaded.AssetId, false))!.Status.Should().Be(ObjectAssetStatus.Active);
        (await check.AssetIdentities.CountAsync()).Should().Be(1);
        var page = await checkScope.ServiceProvider.GetRequiredService<IObjectAssetPagedReader>().GetTemporaryPageAsync(row.TemporaryBindingId!.Value);
        page.Items.Single().ContentAvailability.Should().Be(ObjectAssetContentAvailability.Missing);
    }

    [TestCase("osa")]
    [TestCase("osa'available]state")]
    public async Task AvailabilityMigration_ShouldPreserveV020AssetsAndGuardPopulatedDowngrade(string schema)
    {
        const string baseline = "20261025204842_ScaleIntegrityMaintenance";
        await using var fixture = await SqlServerFixture.CreateAsync(schema: schema, useMigrations: true, targetMigration: baseline);
        using var scope = fixture.RootProvider.CreateScope(); var sp = scope.ServiceProvider;
        var upload = await sp.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("upgrade"), "a.txt", "text/plain");
        var db = sp.GetRequiredService<ObjectStorageAssetDbContext>();
        var before = (await db.FindAssetAsync(upload.AssetId, false))!;
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
        await migrator.MigrateAsync();
        var after = (await db.FindAssetAsync(upload.AssetId, false))!;
        after.ObjectKey.Should().Be(before.ObjectKey); after.Sha256.Should().Be(before.Sha256);
        after.TemporaryBindingId.Should().Be(before.TemporaryBindingId); after.Status.Should().Be(before.Status);
        after.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.NotChecked);
        var availability = sp.GetRequiredService<IObjectAssetAvailabilityService>();
        (await availability.GetAsync(upload.AssetId))!.Availability.Should().Be(ObjectAssetContentAvailability.Unknown);
        (await db.ContentStates.CountAsync()).Should().Be(0);
        fixture.StorageProvider.RemoveObject(after.ObjectKey);
        (await availability.RecheckAsync(upload.AssetId))!.Availability.Should().Be(ObjectAssetContentAvailability.Missing);
        Func<Task> down = () => migrator.MigrateAsync(baseline);
        await down.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>().WithMessage("*availability*blocked*");
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.ContentStates.CountAsync()).Should().Be(1);
        (await db.AssetIdentities.CountAsync()).Should().Be(1);
        await db.ContentStates.ExecuteDeleteAsync(); // Generated disposable test records only.
        await migrator.MigrateAsync(baseline); // An empty observation table can be removed without losing state.
        await migrator.MigrateAsync();
        (await availability.GetAsync(upload.AssetId))!.Availability.Should().Be(ObjectAssetContentAvailability.Unknown);
    }

    [Test]
    public async Task Availability_ShouldIgnoreTransientErrorsAndSupportExplicitRestorationAndArchive()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope(); var sp = scope.ServiceProvider;
        var binding = Guid.NewGuid();
        var upload = await sp.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(binding)
            .AddAsync(OrderAssets.Attachments, CreateStream("restored"), "r.txt", "text/plain");
        var db = sp.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await db.FindAssetAsync(upload.AssetId, false))!;
        var reader = sp.GetRequiredService<IObjectAssetContentReader>();
        var availability = sp.GetRequiredService<IObjectAssetAvailabilityService>();
        fixture.StorageProvider.ThrowOnGet = true;
        Func<Task> read = async () => { var content = await reader.OpenReadAsync(upload.AssetId); if (content is not null) await content.Content.DisposeAsync(); };
        (await read.Should().ThrowAsync<IOException>()).Which.Should().NotBeOfType<ObjectAssetContentMissingException>();
        (await db.ContentStates.CountAsync()).Should().Be(0);
        fixture.StorageProvider.ThrowOnGet = false;
        await read(); (await db.ContentStates.CountAsync()).Should().Be(0); // Healthy reads do not write observations.
        fixture.StorageProvider.RemoveObject(asset.ObjectKey);
        await read.Should().ThrowAsync<ObjectAssetContentMissingException>();
        var firstMissing = (await availability.GetAsync(upload.AssetId))!;
        fixture.StorageProvider.ThrowOnStat = true;
        Func<Task> recheck = async () => { await availability.RecheckAsync(upload.AssetId); };
        await recheck.Should().ThrowAsync<IOException>();
        (await availability.GetAsync(upload.AssetId)).Should().Be(firstMissing);
        fixture.StorageProvider.ThrowOnStat = false;
        (await availability.RecheckAsync(upload.AssetId))!.MissingSinceUtc.Should().Be(firstMissing.MissingSinceUtc);
        await fixture.StorageProvider.PutObjectAsync(new() { BucketName = asset.BucketName, ObjectKey = asset.ObjectKey,
            Content = CreateStream("restored"), ContentLength = 8, ContentType = "text/plain" });
        await read.Should().ThrowAsync<ObjectAssetContentMissingException>(); // Restoration requires an explicit recheck.
        (await availability.RecheckAsync(upload.AssetId))!.Availability.Should().Be(ObjectAssetContentAvailability.Available);
        await read();
        Func<Task> archiveHealthy = async () => { await availability.ArchiveMissingAsync(upload.AssetId); };
        await archiveHealthy.Should().ThrowAsync<InvalidOperationException>();
        fixture.StorageProvider.RemoveObject(asset.ObjectKey);
        (await availability.RecheckAsync(upload.AssetId))!.Availability.Should().Be(ObjectAssetContentAvailability.Missing);
        (await availability.ArchiveMissingAsync(upload.AssetId)).Should().BeTrue();
        (await db.ArchivedAssets.AsNoTracking().SingleAsync()).Id.Should().Be(upload.AssetId);
        (await db.AssetIdentities.CountAsync()).Should().Be(1);
        (await db.ContentStates.CountAsync()).Should().Be(1);
        (await reader.OpenReadAsync(upload.AssetId)).Should().BeNull();
        Func<Task> archivedRead = async () => { await sp.GetRequiredService<IObjectAssetPagedReader>().OpenArchivedReadAsync(upload.AssetId); };
        await archivedRead.Should().ThrowAsync<ObjectAssetContentMissingException>();
    }

    [Test]
    public async Task Availability_ShouldNotApplyAnObservationToDifferentCaseSensitiveContentVersion()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope(); var sp = scope.ServiceProvider;
        var bindingId = Guid.NewGuid();
        var upload = await sp.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(bindingId)
            .AddAsync(OrderAssets.Attachments, CreateStream("versioned"), "v.txt", "text/plain");
        var db = sp.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await db.FindAssetAsync(upload.AssetId))!;
        asset.RecordStorageVersion(asset.ETag, "VersionA"); await db.SaveChangesAsync();
        fixture.StorageProvider.RemoveObject(asset.ObjectKey);
        (await sp.GetRequiredService<IObjectAssetAvailabilityService>().RecheckAsync(upload.AssetId))!.Availability.Should().Be(ObjectAssetContentAvailability.Missing);
        asset.RecordStorageVersion(asset.ETag, "versiona"); await db.SaveChangesAsync();
        await fixture.StorageProvider.PutObjectAsync(new() { BucketName = asset.BucketName, ObjectKey = asset.ObjectKey,
            Content = CreateStream("versioned"), ContentLength = 9, ContentType = "text/plain" });
        (await sp.GetRequiredService<IObjectAssetPagedReader>().GetTemporaryPageAsync(bindingId)).Items.Single()
            .ContentAvailability.Should().Be(ObjectAssetContentAvailability.Unknown);
        (await sp.GetRequiredService<IObjectAssetAvailabilityService>().GetAsync(upload.AssetId))!.Availability.Should().Be(ObjectAssetContentAvailability.Unknown);
        var content = await sp.GetRequiredService<IObjectAssetContentReader>().OpenReadAsync(upload.AssetId);
        await content!.Content.DisposeAsync();
    }

    [Test]
    public async Task Reconciliation_ShouldPersistMissingWithoutChangingOwnershipAndSkipKnownMissing()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope(); var sp = scope.ServiceProvider;
        var bindingId = Guid.NewGuid();
        var upload = await sp.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(bindingId)
            .AddAsync(OrderAssets.Attachments, CreateStream("reconcile"), "a.txt", "text/plain");
        var db = sp.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = (await db.FindAssetAsync(upload.AssetId, false))!;
        fixture.StorageProvider.RemoveObject(asset.ObjectKey);
        var page = await sp.GetRequiredService<IObjectAssetReconciliationReader>().ReconcilePageAsync();
        page.Items.Single().Missing.Should().BeTrue();
        (await sp.GetRequiredService<IObjectAssetAvailabilityService>().GetAsync(upload.AssetId))!.Availability.Should().Be(ObjectAssetContentAvailability.Missing);
        var report = await sp.GetRequiredService<IObjectAssetMaintenanceService>().ReconcileAsync();
        report.MissingActiveAssetIds.Should().Contain(upload.AssetId);
        (await db.ContentStates.CountAsync()).Should().Be(1);
        (await db.FindAssetAsync(upload.AssetId, false))!.TemporaryBindingId.Should().Be(bindingId);
    }

    [Test]
    public async Task AvailabilityStore_ShouldCoalesceConcurrentMissingAndRejectOlderObservations()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope(); var sp = scope.ServiceProvider;
        var upload = await sp.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("race"), "a.txt", "text/plain");
        var db = sp.GetRequiredService<ObjectStorageAssetDbContext>(); var asset = (await db.FindAssetAsync(upload.AssetId, false))!;
        var store = sp.GetRequiredService<ObjectAssetAvailabilityStore>(); var now = DateTimeOffset.UtcNow;
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => store.RecordAsync(asset, ObjectAssetContentAvailability.Missing,
            now, ObjectAssetMissingReason.ObjectNotFound, default)));
        (await db.ContentStates.CountAsync()).Should().Be(1);
        await store.RecordAsync(asset, ObjectAssetContentAvailability.Available, now.AddSeconds(2), null, default);
        await store.RecordAsync(asset, ObjectAssetContentAvailability.Missing, now.AddSeconds(1), ObjectAssetMissingReason.ObjectNotFound, default);
        (await store.GetAsync(asset, default)).Availability.Should().Be(ObjectAssetContentAvailability.Available);
    }
}
