using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class EnhancementContractTests
{
    [Test]
    public void IntegrityDefaults_ShouldBeWarningExplicitAndNotChecked()
    {
        var options = new ObjectStorageAssetOptions();
        options.Integrity.FailureBehavior.Should().Be(Sha256FailureBehavior.Warning);
        options.Integrity.Triggers.Should().Be(Sha256VerificationTriggers.ExplicitOnly);
        NewAsset().Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.NotChecked);
    }

    [Test]
    public void NamingTokens_ShouldRenderInvariantUtcAndPreserveFullIdentity()
    {
        var id = Guid.NewGuid();
        var profile = new ObjectKeyNamingProfile("files", 2,
            "{StorageNamespace}/{Year}/{Month}/{Day}/{Hour}/{Minute}/{Second}/{Millisecond}/{AssetId}{Extension}");
        var context = new ObjectKeyContext(id, "My Files", "File.PDF", ".PDF",
            new DateTimeOffset(2026, 10, 21, 23, 12, 13, 14, TimeSpan.FromHours(3)));
        profile.Render(context).Should().Be($"my-files/2026/10/21/20/12/13/014/{id:N}.pdf");
        ObjectKeyTokens.Definitions[ObjectKeyToken.Hour].Format.Should().Be("HH");
    }

    [TestCase("{Unknown}/{AssetId}")]
    [TestCase("{Year}/{OriginalFileNameStem}")]
    [TestCase("../{AssetId}")]
    [TestCase("/{AssetId}")]
    [TestCase("{AssetId:short}")]
    public void NamingTemplates_ShouldRejectInvalidDefinitions(string template)
        => FluentActions.Invoking(() => new ObjectKeyNamingProfile("files", 1, template).Validate()).Should().Throw<InvalidOperationException>();

    [Test]
    public void ArchiveMove_ShouldPreserveIntegrityAndExpectedHash()
    {
        var asset = NewAsset(); var expected = asset.Sha256; var key = asset.ObjectKey;
        asset.RecordSha256Validation(Sha256ValidationStatus.Mismatch, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            new string('b', 64), "version-1", "HashOrLengthMismatch");
        asset.MarkDeleted(DateTimeOffset.UtcNow, false);
        var archived = asset.CopyToLocation(ObjectAssetLocation.Archive);
        archived.Should().BeOfType<ArchivedObjectAsset>();
        archived.Id.Should().Be(asset.Id); archived.ObjectKey.Should().Be(key);
        archived.Sha256.Should().Be(expected); archived.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
        archived.Sha256ValidationActualHash.Should().Be(new string('b', 64));
    }

    [Test]
    public void OlderVerification_ShouldNotOverwriteNewerResult()
    {
        var asset = NewAsset(); var now = DateTimeOffset.UtcNow;
        asset.RecordSha256Validation(Sha256ValidationStatus.Mismatch, now, now, new string('b', 64), null, null);
        asset.RecordSha256Validation(Sha256ValidationStatus.Valid, now.AddMinutes(-1), now, asset.Sha256, null, null);
        asset.Sha256ValidationStatus.Should().Be(Sha256ValidationStatus.Mismatch);
    }

    [Test]
    public void ExpiredTemporaryBinding_ShouldRemainUnbindable()
    {
        var asset = NewAsset();
        asset.BindTemporary("order", "files", ObjectAssetSlotMultiplicity.Many, Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-1));
        FluentActions.Invoking(() => asset.BindOwner(42L)).Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void ExternalOwnership_ShouldNotPopulateLocalOwnerKeys()
    {
        var asset = NewAsset(); var origin = Guid.NewGuid();
        asset.SetExternalOwnership(Guid.NewGuid(), "order", ObjectAssetOwnerKeyKind.Int64, "42", origin, asset.Id, Guid.Empty);
        asset.BindingKind.Should().Be(ObjectAssetBindingKind.ExternalOwner);
        asset.OwnerKeyInt64.Should().BeNull(); asset.ExternalOwnerKey.Should().Be("42");
        asset.BindOwner(42L); asset.BindingKind.Should().Be(ObjectAssetBindingKind.LocalOwner);
        asset.OriginInstanceId.Should().Be(origin); asset.ExternalOwnerKey.Should().BeNull();
    }

    [TestCase("osa_custom")]
    [TestCase("osa]custom")]
    [TestCase("osa'custom")]
    public void CustomSchemaMigrationScript_ShouldRemapHistoricalAndNewOperations(string schema)
    {
        using var db = Context(schema);
        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        var quoted = "[" + schema.Replace("]", "]]") + "]";
        script.Should().Contain(quoted + ".[ObjectAssets]").And.Contain(quoted + ".[ObjectAssetTemporary]");
        script.Should().NotContain("[osa].[");
    }

    [Test]
    public void KeysetQuery_ShouldTranslateToSqlAndUseOnlyMainTable()
    {
        using var db = Context();
        var query = ObjectAssetPagedReader.ApplyCursor(db.PermanentAssets.Cast<ObjectAsset>(), new(DateTimeOffset.UtcNow, Guid.NewGuid()));
        var sql = query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(101).ToQueryString();
        sql.Should().Contain("ObjectAssets").And.NotContain("ObjectAssetTemporary").And.NotContain("UNION");
    }

    [Test]
    public async Task Staging_ShouldPinNamingAtAcceptanceAndReleaseQuotaOnDispose()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "staging", Guid.NewGuid().ToString("N"));
        var options = new ObjectStorageAssetRuntimeOptions
        {
            DefaultStorageNamespace = "files",
            Naming = new() { ActiveProfile = new("files", 1, "old/{AssetId}{Extension}") },
            Buffering = new() { TemporaryDirectory = directory, MaximumFileBytes = 12, MaximumStagedBytes = 20 }
        };
        var stager = new ObjectAssetStager(options, new DefaultObjectKeyStrategy(options));
        var first = await stager.StageAsync(new MemoryStream(new byte[10]), "a.txt", null, null, null, default);
        options.Naming.ActiveProfile = new("files", 2, "new/{AssetId}{Extension}");
        first.ObjectKey.Should().StartWith("old/"); first.NamingProfile!.Version.Should().Be(1);
        Func<Task> exceed = async () => { await using var ignored = await stager.StageStreamAsync(new MemoryStream(new byte[12]), default); };
        await exceed.Should().ThrowAsync<IOException>();
        await first.DisposeAsync();
        await using (var next = await stager.StageStreamAsync(new MemoryStream(new byte[12]), default)) next.Length.Should().Be(12);
        Directory.GetFiles(directory).Should().BeEmpty();
        Directory.Delete(directory);
    }

    internal static ObjectAsset NewAsset() => ObjectAsset.CreatePendingUpload(Guid.NewGuid(), "order", "files",
        ObjectAssetSlotMultiplicity.Many, "bucket", "files", $"files/{Guid.NewGuid():N}.txt", "file.txt", ".txt", "text/plain",
        DateTimeOffset.UtcNow, 3, new string('a', 64));

    internal static ObjectStorageAssetDbContext Context(string schema = "osa") => new(
        new DbContextOptionsBuilder<ObjectStorageAssetDbContext>().UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=OsaModelOnly;Integrated Security=True;TrustServerCertificate=True;",
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options, new ObjectStorageAssetSchema(schema));
}
