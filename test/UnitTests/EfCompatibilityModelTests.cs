using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed class EfCompatibilityModelTests
{
    private static DbContextOptions<ObjectStorageAssetDbContext> Options() => new DbContextOptionsBuilder<ObjectStorageAssetDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=OfflineModelInspection;Integrated Security=True;Connect Timeout=1").Options;

    [TestCase("osa")]
    [TestCase("tenant_assets")]
    [TestCase("osa'quoted]schema")]
    public void ConfiguredSchema_ShouldMatchSnapshotWithoutChangingMigrationIdentity(string schema)
    {
        using var db = new ObjectStorageAssetDbContext(Options(), new ObjectStorageAssetSchema(schema));
        db.Database.HasPendingModelChanges().Should().BeFalse(); // Metadata only; never opens a connection.
        var migrations = db.GetService<IMigrationsAssembly>();
        migrations.ModelSnapshot!.GetType().Name.Should().Be("ObjectStorageAssetDbContextModelSnapshot");
        migrations.ModelSnapshot.Model.GetDefaultSchema().Should().Be(schema);
        migrations.Migrations.Should().HaveCount(14);
        migrations.FindMigrationId("TrackContentAvailability").Should().Be("20261025230349_TrackContentAvailability");
        using var other = new ObjectStorageAssetDbContext(Options(), new ObjectStorageAssetSchema("other"));
        other.Database.HasPendingModelChanges().Should().BeFalse();
        migrations.ModelSnapshot.Model.GetDefaultSchema().Should().Be(schema, "snapshot schema must be context-scoped");
    }

    [TestCase("osa")]
    [TestCase("osa'quoted]schema")]
    public void GenuineModelChanges_ShouldStillBeDetected(string schema)
    {
        var options = new DbContextOptionsBuilder<ObjectStorageAssetDbContext>(Options())
            .ReplaceService<IModelCustomizer, ChangedModelCustomizer>().Options;
        using var db = new ObjectStorageAssetDbContext(options, new ObjectStorageAssetSchema(schema));
        db.Database.HasPendingModelChanges().Should().BeTrue("schema adaptation must not suppress real model drift");
    }

    public sealed class ChangedModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<PermanentObjectAsset>().Property<string>("UnexpectedCompatibilityColumn");
        }
    }
}
