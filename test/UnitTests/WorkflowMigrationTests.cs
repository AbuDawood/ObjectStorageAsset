using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed partial class WorkflowTests
{
    private const string V016Migration = "20260308065818_AddObjectAssetCustomMetadata";
    private const string ReserveMigration = "20261021052301_ReserveAssetIdentities";
    private const string SplitMigration = "20261021052310_SplitAssetLifecycle";

    [Test]
    public async Task UpgradeIdempotentScript_ShouldApplyTwiceWithoutChangingData()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(useMigrations: true, targetMigration: V016Migration);
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        await InsertLegacyAsync(db, "osa", temporary: true);
        var script = db.GetService<IMigrator>().GenerateScript(V016Migration, options: MigrationsSqlGenerationOptions.Idempotent);
        await db.Database.OpenConnectionAsync();
        for (var run = 0; run < 2; run++)
            foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = batch; command.CommandTimeout = 120;
                await command.ExecuteNonQueryAsync();
            }
        (await db.TemporaryAssets.CountAsync()).Should().Be(1);
        (await db.AssetIdentities.CountAsync()).Should().Be(1);
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        await db.Database.CloseConnectionAsync();
    }

    [TestCase("osa")]
    [TestCase("osa]upgrade")]
    [TestCase("osa'upgrade")]
    public async Task UpgradeFromV016_ShouldPreserveAndClassifyEveryLegacyRecord(string schema)
    {
        await using var fixture = await SqlServerFixture.CreateAsync(schema: schema, useMigrations: true, targetMigration: V016Migration);
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var local = await InsertLegacyAsync(db, schema, ownerKey: 42);
        var temporary = await InsertLegacyAsync(db, schema, temporary: true);
        var noExpiry = await InsertLegacyAsync(db, schema, temporary: true, temporaryHasExpiry: false);
        var retained = await InsertLegacyAsync(db, schema, deleted: true, ownerKey: 43);
        var physical = await InsertLegacyAsync(db, schema, deleted: true, physicallyDeleted: true, ownerKey: 44);
        var unresolved = await InsertLegacyAsync(db, schema);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(ReserveMigration);
        (await db.AssetIdentities.CountAsync()).Should().Be(6);
        (await db.PermanentAssets.CountAsync()).Should().Be(6);
        foreach (var hosted in fixture.RootProvider.GetServices<IHostedService>())
        {
            Func<Task> notReady = () => hosted.StartAsync(default);
            await notReady.Should().ThrowAsync<InvalidOperationException>();
        }
        await migrator.MigrateAsync();
        (await db.PermanentAssets.CountAsync()).Should().Be(2);
        (await db.TemporaryAssets.CountAsync()).Should().Be(2);
        (await db.ArchivedAssets.CountAsync()).Should().Be(2);
        (await db.FindAssetAsync(local))!.BindingKind.Should().Be(ObjectAssetBindingKind.LocalOwner);
        (await db.FindAssetAsync(temporary))!.BindingKind.Should().Be(ObjectAssetBindingKind.Temporary);
        (await db.FindAssetAsync(noExpiry))!.TemporaryBindingExpiresAtUtc.Should().BeNull();
        var legacy = (await db.FindAssetAsync(unresolved))!;
        legacy.BindingKind.Should().Be(ObjectAssetBindingKind.LegacyUnresolved);
        legacy.OriginInstanceId.Should().BeNull(); legacy.NamingProfileId.Should().BeNull();
        (await db.FindAssetAsync(retained))!.PhysicalDeletedAtUtc.Should().BeNull();
        (await db.FindAssetAsync(physical))!.PhysicalDeletedAtUtc.Should().NotBeNull();
        var all = await db.ObjectAssets.AsNoTracking().ToListAsync();
        all.Should().OnlyContain(x => x.Sha256ValidationStatus == Sha256ValidationStatus.NotChecked
            && x.Sha256 == new string('a', 64) && x.CustomMetadataJson == "{\"legacy\":\"preserved\"}" && x.ProviderVersionId == "version-legacy");
        (await db.UpgradeState.SingleAsync(x => x.Name == "LifecycleSplitV1")).Completed.Should().BeTrue();
        foreach (var hosted in fixture.RootProvider.GetServices<IHostedService>()) await hosted.StartAsync(default);
        await migrator.MigrateAsync(); // Applying the completed upgrade again changes nothing.
        (await db.AssetIdentities.CountAsync()).Should().Be(6);
        var pendingDelete = (await db.FindAssetAsync(local))!;
        pendingDelete.MarkPendingDelete(DateTimeOffset.UtcNow, true);
        await db.SaveChangesAsync();
        Func<Task> downgrade = () => migrator.MigrateAsync(V016Migration);
        await downgrade.Should().ThrowAsync<SqlException>().WithMessage("*downgrade*blocked*");
        (await db.ObjectAssets.CountAsync()).Should().Be(6);
        (await db.PermanentAssets.AsNoTracking().SingleAsync(x => x.Id == local)).PhysicalDeletionRequested.Should().BeTrue();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task Upgrade_ShouldPauseForHostForeignKeysAndPreserveReferencesAfterRetargeting()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(useMigrations: true, targetMigration: V016Migration);
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var id = await InsertLegacyAsync(db, "osa", deleted: true, ownerKey: 42);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [dbo].[AssetUsage] ([Id] int PRIMARY KEY,[AssetId] uniqueidentifier NOT NULL, CONSTRAINT [FK_Usage_Payload] FOREIGN KEY ([AssetId]) REFERENCES [osa].[ObjectAssets]([Id]));");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT [dbo].[AssetUsage] ([Id],[AssetId]) VALUES (1,{id});");
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(ReserveMigration);
        Func<Task> blocked = () => migrator.MigrateAsync();
        await blocked.Should().ThrowAsync<SqlException>().WithMessage("*Retarget host foreign keys*");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [dbo].[AssetUsage] DROP CONSTRAINT [FK_Usage_Payload]; ALTER TABLE [dbo].[AssetUsage] ADD CONSTRAINT [FK_Usage_Identity] FOREIGN KEY ([AssetId]) REFERENCES [osa].[ObjectAssetIdentity]([AssetId]);");
        await migrator.MigrateAsync();
        (await db.ArchivedAssets.SingleAsync()).Id.Should().Be(id);
        (await db.Database.SqlQueryRaw<Guid>("SELECT [AssetId] AS [Value] FROM [dbo].[AssetUsage]").SingleAsync()).Should().Be(id);
    }

    [Test]
    public async Task Upgrade_ShouldResumeAfterFailureFollowingACommittedSplitBatch()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(useMigrations: true, targetMigration: V016Migration);
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        db.Database.SetCommandTimeout(120);
        for (var i = 0; i < 1001; i++) await InsertLegacyAsync(db, "osa", deleted: true, ownerKey: 42);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(ReserveMigration);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [osa].[FailSecondArchiveBatch] ON [osa].[ObjectAssetArchive] AFTER INSERT AS BEGIN IF (SELECT COUNT_BIG(*) FROM [osa].[ObjectAssetArchive])>1000 THROW 51001, 'Injected second-batch failure', 1; END;");
        Func<Task> interrupted = () => migrator.MigrateAsync();
        await interrupted.Should().ThrowAsync<SqlException>().WithMessage("*Injected second-batch failure*");
        (await db.ArchivedAssets.CountAsync()).Should().Be(1000);
        (await db.PermanentAssets.CountAsync()).Should().Be(1);
        (await db.UpgradeState.AsNoTracking().SingleAsync(x => x.Name == "LifecycleSplitV1")).ProcessedCount.Should().Be(1000);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [osa].[FailSecondArchiveBatch];");
        await migrator.MigrateAsync();
        (await db.ArchivedAssets.CountAsync()).Should().Be(1001);
        (await db.PermanentAssets.CountAsync()).Should().Be(0);
        (await db.AssetIdentities.CountAsync()).Should().Be(1001);
    }

    [Test]
    public async Task Upgrade_ShouldFailForContradictoryLegacyBindingAndAllowRepairThenRerun()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(useMigrations: true, targetMigration: V016Migration);
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var id = await InsertLegacyAsync(db, "osa", ownerKey: 42, temporary: true);
        Func<Task> upgrade = () => db.Database.MigrateAsync();
        await upgrade.Should().ThrowAsync<SqlException>().WithMessage("*Contradictory legacy*");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [osa].[ObjectAssets] SET [TemporaryBindingId]=NULL,[TemporaryBindingExpiresAtUtc]=NULL WHERE [Id]={id};");
        await db.Database.MigrateAsync();
        (await db.PermanentAssets.SingleAsync()).BindingKind.Should().Be(ObjectAssetBindingKind.LocalOwner);
    }

    [Test]
    public async Task UpgradeValidation_ShouldRejectMissingPayloadAfterSplitting()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(useMigrations: true, targetMigration: V016Migration);
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        await InsertLegacyAsync(db, "osa", deleted: true, ownerKey: 42);
        await db.GetService<IMigrator>().MigrateAsync(SplitMigration);
        await db.ArchivedAssets.ExecuteDeleteAsync();
        Func<Task> validate = () => db.Database.MigrateAsync();
        await validate.Should().ThrowAsync<SqlException>().WithMessage("*conservation failed*");
        (await db.UpgradeState.AsNoTracking().SingleAsync(x => x.Name == "LifecycleSplitV1")).Completed.Should().BeFalse();
    }

    // This seed column list is taken from the four published v0.1.6 EF migrations above.
#pragma warning disable EF1002
    private static async Task<Guid> InsertLegacyAsync(ObjectStorageAssetDbContext db, string schema, bool temporary = false,
        bool deleted = false, bool physicallyDeleted = false, long? ownerKey = null, bool temporaryHasExpiry = true)
    {
        var id = Guid.NewGuid(); var created = DateTimeOffset.UtcNow.AddDays(-1);
        var table = "[" + schema.Replace("]", "]]") + "].[ObjectAssets]";
        await db.Database.ExecuteSqlRawAsync($$"""
            INSERT {{table}} ([Id],[OwnerType],[OwnerKeyKind],[OwnerKeyInt64],[SlotName],[SlotMultiplicity],
                [BucketName],[StorageNamespace],[ObjectKey],[OriginalFileName],[Extension],[ContentType],[SizeBytes],[Sha256],
                [OwnershipMode],[CustomMetadataJson],[Status],[CreatedAtUtc],[LastStatusChangedAtUtc],[TemporaryBindingId],
                [TemporaryBindingExpiresAtUtc],[DeletedAtUtc],[PhysicalDeletedAtUtc],[ProviderVersionId])
            VALUES (@id,N'order',@kind,@owner,N'attachments',2,N'legacy',N'legacy',@key,N'legacy.txt',N'.txt',N'text/plain',3,@hash,
                2,@metadata,@status,@created,@created,@temporary,@expires,@deleted,@physical,N'version-legacy');
            """, new SqlParameter("@id", id), new SqlParameter("@kind", ownerKey.HasValue ? 1 : 0),
            new SqlParameter("@owner", (object?)ownerKey ?? DBNull.Value), new SqlParameter("@key", $"legacy/{id:N}.txt"),
            new SqlParameter("@hash", new string('a', 64)), new SqlParameter("@metadata", "{\"legacy\":\"preserved\"}"), new SqlParameter("@status", deleted ? 5 : 2),
            new SqlParameter("@created", created), new SqlParameter("@temporary", temporary ? Guid.NewGuid() : DBNull.Value),
            new SqlParameter("@expires", temporary && temporaryHasExpiry ? created.AddHours(1) : DBNull.Value),
            new SqlParameter("@deleted", deleted ? created : DBNull.Value), new SqlParameter("@physical", physicallyDeleted ? created : DBNull.Value));
        return id;
    }
#pragma warning restore EF1002
}
