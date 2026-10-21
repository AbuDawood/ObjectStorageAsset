using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed partial class WorkflowTests
{
    [Test]
    public async Task RetriedTemporaryRemoval_ShouldRetainItsExplicitPhysicalDeleteIntent()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(physicallyDeleteRequestedAssets: true, physicallyDeleteExpiredAssets: false);
        using var scope = fixture.RootProvider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid());
        var uploaded = await session.AddAsync(OrderAssets.Attachments, CreateStream("remove"), "a.txt", "text/plain");
        fixture.StorageProvider.ThrowOnDelete = true;
        Func<Task> remove = () => session.RemoveAsync(uploaded.AssetId);
        await remove.Should().ThrowAsync<InvalidOperationException>();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await db.TemporaryAssets.AsNoTracking().SingleAsync()).PhysicalDeletionRequested.Should().BeTrue();
        await db.TemporaryAssets.ExecuteUpdateAsync(x => x.SetProperty(a => a.NextDeleteAttemptAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        db.ChangeTracker.Clear(); fixture.StorageProvider.ThrowOnDelete = false;
        (await scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>().RetryDeleteFailuresAsync()).Should().Be(1);
        (await db.ArchivedAssets.SingleAsync()).PhysicalDeletedAtUtc.Should().NotBeNull();
        fixture.StorageProvider.ObjectKeys.Should().BeEmpty();
    }

    [Test]
    public async Task NamingProfileActivation_ShouldPersistIndependentlyOfCallerRollback()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var naming = fixture.RootProvider.GetRequiredService<IObjectAssetNamingService>();
        using (var transaction = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            await naming.ActivateAsync(new("durable", 1, "files/{AssetId}{Extension}"));
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await db.NamingProfiles.SingleAsync()).ProfileId.Should().Be("durable");
        Func<Task> redefine = () => naming.ActivateAsync(new("durable", 1, "changed/{AssetId}{Extension}"));
        await redefine.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
    }

    [Test]
    public async Task LegacyPeerImport_ShouldRemainPermanentAndPreserveEnrichedOwnershipAcrossV2RoundTrip()
    {
        var authority = Guid.NewGuid();
        await using var fixture = await SqlServerFixture.CreateAsync(configure: o =>
        { o.LocalInstanceId = Guid.NewGuid(); o.StorageAuthorityId = authority; });
        using var scope = fixture.RootProvider.CreateScope();
        var descriptor = CreateDescriptor(Guid.NewGuid(), "legacy.txt", "text/plain", 3, new string('a', 64), "legacy/file.txt", expiresAtUtc: DateTimeOffset.UtcNow.AddDays(-1));
        var request = new ObjectAssetDescriptorRegistrationRequest { Descriptor = descriptor, OwnershipMode = ObjectAssetOwnershipMode.Referenced };
        var peerOwner = new ObjectAssetOwnerReference(Guid.NewGuid(), "order", ObjectAssetOwnerKeyKind.Int64, "42");
        var peer = new ObjectAssetLegacyPeerContext(authority, Owner: peerOwner);
        var exchange = scope.ServiceProvider.GetRequiredService<IObjectAssetExchangeRegistry>();
        (await exchange.RegisterLegacyDescriptorAsync(request, peer)).Outcome.Should().Be(ObjectAssetDescriptorRegistrationOutcome.Added);
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = await db.PermanentAssets.SingleAsync();
        asset.BindingKind.Should().Be(ObjectAssetBindingKind.ExternalOwner); asset.OriginInstanceId.Should().BeNull();
        asset.ExpiresAtUtc.Should().BeNull();
        (await scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>().ExpireAssetsAsync()).Should().Be(0);
        (await scope.ServiceProvider.GetRequiredService<IObjectAssetRegistry>().RegisterDescriptorAsync(request)).Outcome
            .Should().Be(ObjectAssetDescriptorRegistrationOutcome.IgnoredIdentical);
        var origin = new ObjectAssetOriginReference(Guid.NewGuid(), descriptor.AssetId);
        await exchange.RegisterLegacyDescriptorAsync(request, peer with { Origin = origin });
        var v3 = await exchange.GetDescriptorV3Async(asset.Id);
        v3!.Storage.AuthorityId.Should().Be(authority); v3.Origin.Should().Be(origin); v3.Owner.Should().Be(peerOwner);
        var conflict = await exchange.RegisterLegacyDescriptorAsync(request, peer with { Origin = new(Guid.NewGuid(), descriptor.AssetId) });
        conflict.Outcome.Should().Be(ObjectAssetDescriptorRegistrationOutcome.RejectedConflict); conflict.ConflictFields.Should().Contain("Origin");
        var externalPage = await scope.ServiceProvider.GetRequiredService<IObjectAssetPagedReader>().GetExternalOwnerPageAsync(peerOwner);
        externalPage.Items.Should().ContainSingle();
        var bound = await scope.ServiceProvider.GetRequiredService<IObjectAssetOwnerBinder>().BindRegisteredAsync(asset.Id, new Order { Id = 42 }, OrderAssets.Attachments);
        bound.AssetId.Should().Be(asset.Id);
        var local = await db.PermanentAssets.AsNoTracking().SingleAsync();
        local.BindingKind.Should().Be(ObjectAssetBindingKind.LocalOwner); local.OriginInstanceId.Should().Be(origin.InstanceId);
        local.ObjectKey.Should().Be(descriptor.ObjectKey); local.OwnershipMode.Should().Be(ObjectAssetOwnershipMode.Referenced);
    }

    [Test]
    public async Task Summaries_ShouldSupportGuidAndParameterizedTextOwnerKeys()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: o =>
        { o.AddBinding<GuidOwnerAssets>(); o.AddBinding<TextOwnerAssets>(); });
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var guid = Guid.NewGuid(); var text = "owner'with{braces}";
        var guidAsset = EnhancementContractTests.NewAsset(); guidAsset.SetOwnerSlot("guid-owner", "files", ObjectAssetSlotMultiplicity.Many);
        guidAsset.BindOwner(guid); guidAsset.MarkUploadSucceeded(DateTimeOffset.UtcNow, null, null);
        var textAsset = EnhancementContractTests.NewAsset(); textAsset.SetOwnerSlot("text-owner", "files", ObjectAssetSlotMultiplicity.Many);
        textAsset.BindOwner(text); textAsset.MarkUploadSucceeded(DateTimeOffset.UtcNow, null, null);
        db.ObjectAssets.AddRange(guidAsset, textAsset); await db.SaveChangesAsync();
        var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetReader>();
        var guidResult = await reader.GetSummariesAsync<GuidOwner, Guid>([guid, Guid.NewGuid()], ObjectAssetSlot.Many("files"));
        guidResult.Should().HaveCount(1); guidResult[guid].Count.Should().Be(1); guidResult[guid].First!.AssetId.Should().Be(guidAsset.Id);
        var textResult = await reader.GetSummariesAsync<TextOwner, string>([text, "missing"], ObjectAssetSlot.Many("files"));
        textResult.Should().HaveCount(1); textResult[text].Count.Should().Be(1); textResult[text].First!.AssetId.Should().Be(textAsset.Id);
    }

    [Test]
    public async Task PendingMaintenance_ShouldPreserveVirtualDeletionAfterInterruptedExpiry()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(physicallyDeleteExpiredAssets: false);
        using var scope = fixture.RootProvider.CreateScope();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("retained"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = await db.FindAssetAsync(uploaded.AssetId); asset!.MarkPendingDelete(DateTimeOffset.UtcNow); await db.SaveChangesAsync();
        fixture.StorageProvider.ThrowOnDelete = true;
        await scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>().ProcessPendingDeletesAsync();
        var archived = await db.ArchivedAssets.SingleAsync(); archived.PhysicalDeletedAtUtc.Should().BeNull();
        fixture.StorageProvider.ContainsObject(archived.ObjectKey).Should().BeTrue();
        var retained = await scope.ServiceProvider.GetRequiredService<IObjectAssetPagedReader>().OpenArchivedReadAsync(uploaded.AssetId);
        retained.Should().NotBeNull(); await retained!.Content.DisposeAsync();
    }

    [Test]
    public async Task AddedTemporaryRecord_ShouldBeStoredInItsCorrectLifecycleTable()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = EnhancementContractTests.NewAsset(); asset.BindTemporary("order", "files", ObjectAssetSlotMultiplicity.Many, Guid.NewGuid(), null);
        db.ObjectAssets.Add(asset); await db.SaveChangesAsync();
        (await db.PermanentAssets.CountAsync()).Should().Be(0);
        (await db.TemporaryAssets.SingleAsync()).Id.Should().Be(asset.Id);
        (await db.FindAssetAsync(asset.Id))!.BindingKind.Should().Be(ObjectAssetBindingKind.Temporary);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConcurrentBindingAndDeletion_ShouldRejectStaleTransition(bool bindingFirst)
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var setup = fixture.RootProvider.CreateScope();
        var uploaded = await setup.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>()
            .For<Order>(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1))
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        using var a = fixture.RootProvider.CreateScope(); using var b = fixture.RootProvider.CreateScope();
        var first = a.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var second = b.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var binding = await first.TemporaryAssets.SingleAsync(); var deleting = await second.TemporaryAssets.SingleAsync();
        if (bindingFirst)
        {
            binding.BindOwner(42L); await first.SaveChangesAsync();
            deleting.MarkPendingDelete(DateTimeOffset.UtcNow);
            Func<Task> stale = async () => await second.SaveChangesAsync();
            await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
            (await first.PermanentAssets.CountAsync(x => x.Id == uploaded.AssetId)).Should().Be(1);
        }
        else
        {
            deleting.MarkPendingDelete(DateTimeOffset.UtcNow); await second.SaveChangesAsync();
            binding.BindOwner(42L);
            Func<Task> stale = async () => await first.SaveChangesAsync();
            await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
            (await second.TemporaryAssets.AsNoTracking().SingleAsync()).Status.Should().Be(ObjectAssetStatus.PendingDelete);
        }
        fixture.StorageProvider.ObjectKeys.Should().ContainSingle();
    }

    [Test]
    public async Task GuardResult_ShouldSurviveCallerTransactionRollback()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: options =>
        { options.Integrity.FailureBehavior = Sha256FailureBehavior.Guard; options.Integrity.Triggers = Sha256VerificationTriggers.OnRead; });
        using var scope = fixture.RootProvider.CreateScope();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>()
            .For<Order>(Guid.NewGuid()).AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        fixture.StorageProvider.CorruptOnGet = true;
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Func<Task> read = async () => await scope.ServiceProvider.GetRequiredService<IObjectAssetContentReader>().OpenReadAsync(uploaded.AssetId);
            await read.Should().ThrowAsync<ObjectAssetIntegrityException>();
            await transaction.RollbackAsync();
        }
        using var verify = fixture.RootProvider.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>().TemporaryAssets.AsNoTracking().SingleAsync())
            .Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
    }

    [Test]
    public async Task MaintenanceCycle_ShouldNotRetryItsOwnFailuresEvenWithTinyBackoff()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: options => options.Maintenance.InitialRetryDelay = TimeSpan.FromTicks(1));
        using var setup = fixture.RootProvider.CreateScope();
        await setup.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>()
            .For<Order>(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1))
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        fixture.StorageProvider.ThrowOnDelete = true;
        await fixture.RootProvider.GetRequiredService<IObjectAssetMaintenanceRunner>().RunOnceAsync();
        using var verify = fixture.RootProvider.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>().TemporaryAssets.SingleAsync()).DeleteAttemptCount.Should().Be(1);
    }

    [TestCase(Sha256FailureBehavior.Warning)]
    [TestCase(Sha256FailureBehavior.Guard)]
    public async Task IntegrityRead_ShouldPersistMismatchBeforeWarningOrGuard(Sha256FailureBehavior behavior)
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: options =>
        { options.Integrity.FailureBehavior = behavior; options.Integrity.Triggers = Sha256VerificationTriggers.OnRead; });
        using var scope = fixture.RootProvider.CreateScope();
        var temporary = scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid());
        var uploaded = await temporary.AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var before = await db.ObjectAssets.AsNoTracking().SingleAsync();
        before.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.NotChecked);
        fixture.StorageProvider.CorruptOnGet = true;
        var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetContentReader>();
        if (behavior == Sha256FailureBehavior.Guard)
        {
            Func<Task> read = async () => { var result = await reader.OpenReadAsync(uploaded.AssetId); if (result is not null) await result.Content.DisposeAsync(); };
            await read.Should().ThrowAsync<ObjectAssetIntegrityException>();
        }
        else
        {
            var read = await reader.OpenReadAsync(uploaded.AssetId);
            read!.Integrity!.Status.Should().Be(Sha256ValidationStatus.Mismatch);
            await read.Content.DisposeAsync();
        }
        using var verificationScope = fixture.RootProvider.CreateScope();
        var persisted = await verificationScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>().ObjectAssets.AsNoTracking().SingleAsync();
        persisted.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
        persisted.Sha256.Should().Be(before.Sha256); persisted.Sha256ValidationActualHash.Should().NotBe(before.Sha256);
        persisted.Sha256ValidationCompletedAtUtc.Should().NotBeNull();
        persisted.Status.Should().Be(ObjectAssetStatus.Active);
    }

    [Test]
    public async Task ExplicitVerification_ShouldPersistValid_AndReadFailureShouldPersistFailed()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        using var scope = fixture.RootProvider.CreateScope();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid())
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        var service = scope.ServiceProvider.GetRequiredService<IObjectAssetIntegrityService>();
        (await service.VerifyAsync(uploaded.AssetId)).Status.Should().Be(Sha256ValidationStatus.Valid);
        fixture.StorageProvider.ThrowOnGet = true;
        (await service.VerifyAsync(uploaded.AssetId)).Status.Should().Be(Sha256ValidationStatus.Failed);
        using var verify = fixture.RootProvider.CreateScope();
        var asset = await verify.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>().ObjectAssets.AsNoTracking().SingleAsync();
        asset.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Failed);
        asset.Sha256ValidationActualHash.Should().BeNull();
    }

    [Test]
    public async Task GuardedPostUpload_ShouldPersistMismatchAndLeaveUploadFailed()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: options =>
        { options.Integrity.FailureBehavior = Sha256FailureBehavior.Guard; options.Integrity.Triggers = Sha256VerificationTriggers.AfterUpload; });
        fixture.StorageProvider.CorruptOnGet = true;
        using var scope = fixture.RootProvider.CreateScope();
        var temporary = scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(Guid.NewGuid());
        Func<Task> upload = async () => await temporary.AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        await upload.Should().ThrowAsync<ObjectAssetIntegrityException>();
        using var verify = fixture.RootProvider.CreateScope();
        var asset = await verify.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>().TemporaryAssets.AsNoTracking().SingleAsync();
        asset.Status.Should().Be(ObjectAssetStatus.UploadFailed);
        asset.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
    }

    [Test]
    public async Task LifecycleMoves_ShouldPreserveKeyNamingAndValidation_AndAllowExplicitArchiveRead()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(physicallyDeleteRequestedAssets: false, configure: options =>
        { options.LocalInstanceId = Guid.NewGuid(); options.Naming.ActiveProfile = new("files", 1, "old/{AssetId}{Extension}"); });
        using var scope = fixture.RootProvider.CreateScope();
        var binding = Guid.NewGuid();
        var uploaded = await scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(binding)
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), "a.txt", "text/plain");
        await scope.ServiceProvider.GetRequiredService<IObjectAssetIntegrityService>().VerifyAsync(uploaded.AssetId);
        fixture.RootProvider.GetRequiredService<ObjectStorageAssetOptions>().Naming.ActiveProfile = new("files", 2, "new/{AssetId}{Extension}");
        await using var host = fixture.CreateHostDbContext();
        var order = new Order { Number = "migration" }; host.Orders.Add(order); await host.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IObjectAssetBindingFinalizer>().FinalizeTemporaryBindingAsync(binding, order);
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await db.TemporaryAssets.CountAsync()).Should().Be(0);
        var permanent = await db.PermanentAssets.AsNoTracking().SingleAsync();
        permanent.ObjectKey.Should().StartWith("old/"); permanent.NamingProfileVersion.Should().Be(1);
        permanent.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Valid);
        var sessions = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
        await sessions.For(order).RemoveAsync(uploaded.AssetId);
        await scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>().SaveChangesWithAssetsAsync(host);
        (await db.PermanentAssets.CountAsync()).Should().Be(0);
        var archived = await db.ArchivedAssets.AsNoTracking().SingleAsync();
        archived.ObjectKey.Should().Be(permanent.ObjectKey); archived.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Valid);
        (await db.AssetIdentities.SingleAsync()).Location.Should().Be(ObjectAssetLocation.Archive);
        var archiveReader = scope.ServiceProvider.GetRequiredService<IObjectAssetPagedReader>();
        var content = await archiveReader.OpenArchivedReadAsync(uploaded.AssetId);
        content.Should().NotBeNull(); await content!.Content.DisposeAsync();
        (await scope.ServiceProvider.GetRequiredService<IObjectAssetContentReader>().OpenReadAsync(uploaded.AssetId)).Should().BeNull();
    }

    [Test]
    public async Task Maintenance_ShouldBoundExpiryAndPreserveRetryBackoff()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(configure: options => options.Maintenance.BatchSize = 2);
        using var scope = fixture.RootProvider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IObjectAssetTemporarySessionFactory>();
        for (var i = 0; i < 5; i++) await factory.For<Order>(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1))
            .AddAsync(OrderAssets.Attachments, CreateStream("correct"), $"{i}.txt", "text/plain");
        fixture.StorageProvider.ThrowOnDelete = true;
        var maintenance = scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>();
        (await maintenance.ExpireAssetsAsync()).Should().Be(2);
        (await maintenance.RetryDeleteFailuresAsync()).Should().Be(0);
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        (await db.TemporaryAssets.CountAsync(x => x.Status == ObjectAssetStatus.DeleteFailed)).Should().Be(2);
        (await db.TemporaryAssets.CountAsync(x => x.Status == ObjectAssetStatus.Active)).Should().Be(3);
    }

    [Test]
    public async Task SqlApplicationLock_ShouldSkipContenderAndReleaseForNextRun()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        var guard = fixture.RootProvider.GetRequiredService<IObjectAssetMaintenanceLock>();
        var first = await guard.TryAcquireAsync(); first.Should().NotBeNull();
        (await guard.TryAcquireAsync()).Should().BeNull();
        await first!.DisposeAsync();
        await using var next = await guard.TryAcquireAsync(); next.Should().NotBeNull();
    }

    [Test]
    public async Task V3ExternalImport_ShouldRetainOriginAndAvoidLocalOwnerCollision()
    {
        var sourceId = Guid.NewGuid(); var authority = Guid.NewGuid();
        await using var source = await SqlServerFixture.CreateAsync(configure: options =>
        { options.LocalInstanceId = sourceId; options.StorageAuthorityId = authority; });
        await using var host = source.CreateHostDbContext();
        using var sourceScope = source.RootProvider.CreateScope();
        var order = new Order { Number = "source" }; host.Orders.Add(order);
        await sourceScope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>().For(order)
            .SetSingleAsync(OrderAssets.Invoice, CreateStream("correct"), "a.txt", "text/plain");
        await sourceScope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>().SaveChangesWithAssetsAsync(host);
        var asset = await sourceScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>().PermanentAssets.SingleAsync();
        var exchange = sourceScope.ServiceProvider.GetRequiredService<IObjectAssetExchangeRegistry>();
        var descriptor = await exchange.GetDescriptorV3Async(asset.Id);
        await using var target = await SqlServerFixture.CreateAsync(configure: options =>
        { options.LocalInstanceId = Guid.NewGuid(); options.StorageAuthorityId = authority; }, sharedStorage: source.StorageProvider);
        using var targetScope = target.RootProvider.CreateScope();
        var imported = await targetScope.ServiceProvider.GetRequiredService<IObjectAssetExchangeRegistry>().RegisterDescriptorV3Async(descriptor!);
        imported.Outcome.Should().Be(ObjectAssetDescriptorRegistrationOutcome.Added);
        var copy = await targetScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>().PermanentAssets.SingleAsync();
        copy.BindingKind.Should().Be(ObjectAssetBindingKind.ExternalOwner); copy.OriginInstanceId.Should().Be(sourceId);
        copy.OwnerKeyInt64.Should().BeNull(); copy.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.NotChecked);
        (await targetScope.ServiceProvider.GetRequiredService<IObjectAssetReader>().GetSingleAsync(order, OrderAssets.Invoice)).Should().BeNull();
        var content = await targetScope.ServiceProvider.GetRequiredService<IObjectAssetContentReader>().OpenReadAsync(asset.Id);
        content.Should().NotBeNull(); await content!.Content.DisposeAsync();
        var roundtrip = await targetScope.ServiceProvider.GetRequiredService<IObjectAssetExchangeRegistry>().GetDescriptorV3Async(asset.Id);
        var returned = await exchange.RegisterDescriptorV3Async(roundtrip!, ObjectAssetOwnershipMode.Managed);
        returned.Outcome.Should().Be(ObjectAssetDescriptorRegistrationOutcome.IgnoredIdentical);
        (await sourceScope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>().PermanentAssets.AsNoTracking().SingleAsync())
            .BindingKind.Should().Be(ObjectAssetBindingKind.LocalOwner);
    }
    private sealed class GuidOwner { public Guid Id { get; set; } }
    private sealed class TextOwner { public string Id { get; set; } = ""; }
    private sealed class GuidOwnerAssets : IObjectAssetBinding<GuidOwner, Guid>
    { public void Configure(IObjectAssetOwnerBuilder<GuidOwner, Guid> builder) { builder.OwnerType("guid-owner"); builder.Key(x => x.Id); builder.Slot(ObjectAssetSlot.Many("files")); } }
    private sealed class TextOwnerAssets : IObjectAssetBinding<TextOwner, string>
    { public void Configure(IObjectAssetOwnerBuilder<TextOwner, string> builder) { builder.OwnerType("text-owner"); builder.Key(x => x.Id); builder.Slot(ObjectAssetSlot.Many("files")); } }
}
