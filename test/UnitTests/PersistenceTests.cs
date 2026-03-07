using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class PersistenceTests
{
    [Test]
    public void ObjectAsset_CreatePendingUpload_ShouldSetPendingState()
    {
        var now = DateTimeOffset.UtcNow;
        var asset = ObjectAsset.CreatePendingUpload(
            Guid.NewGuid(),
            ownerType: "order",
            slotName: "invoice",
            slotMultiplicity: ObjectAssetSlotMultiplicity.Single,
            bucketName: "osa-dev",
            storageNamespace: "up-notification",
            objectKey: "up-notification/2026/03/file.pdf",
            originalFileName: "file.pdf",
            extension: ".pdf",
            contentType: "application/pdf",
            createdAtUtc: now);

        asset.Status.Should().Be(ObjectAssetStatus.PendingUpload);
        asset.OwnerType.Should().Be("order");
        asset.ObjectKey.Should().Be("up-notification/2026/03/file.pdf");
    }

    [Test]
    public void ObjectStorageAssetDbContext_ShouldApplyConfiguredSchema()
    {
        var context = CreateContext("osa_test");
        var entityType = context.Model.FindEntityType(typeof(ObjectAsset));

        entityType.Should().NotBeNull();
        entityType!.GetSchema().Should().Be("osa_test");
    }

    [Test]
    public void ObjectStorageAssetDbContext_ShouldAddComputedStatusLabel()
    {
        var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(ObjectAsset))!;
        var statusLabel = entityType.FindProperty("StatusLabel");

        statusLabel.Should().NotBeNull();
        var computedColumnSql = statusLabel!.GetComputedColumnSql();

        computedColumnSql.Should().NotBeNull();
        computedColumnSql.Should().Contain("PendingUpload");
        computedColumnSql.Should().Contain("DeleteFailed");
    }

    private static ObjectStorageAssetDbContext CreateContext(string schema = "osa")
    {
        var options = new DbContextOptionsBuilder<ObjectStorageAssetDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ObjectStorageAssetTests;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        return new ObjectStorageAssetDbContext(options, new ObjectStorageAssetSchema(schema));
    }
}
