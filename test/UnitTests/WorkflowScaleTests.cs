using System.Data;
using System.Diagnostics;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
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
    public async Task LargeDataset_ShouldKeepSummariesCompactAndMaintenanceBounded()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("OSA_SCALE_MAIN_ROWS"), out var count))
            Assert.Ignore("Set OSA_SCALE_MAIN_ROWS (for example 1000000) to run the disposable SQL scale scenario.");
        if (count < 1000 || count > 2_000_000 || count % 1000 != 0) throw new InvalidOperationException("Scale row count must be a multiple of 1,000 within 1,000..2,000,000.");
        const int batch = 200;
        await using var fixture = await SqlServerFixture.CreateAsync(configure: o => o.Maintenance.BatchSize = batch);
        using var scope = fixture.RootProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var timer = Stopwatch.StartNew();
        await BulkSeedAsync(db, count, false);
        var backlog = count / 10;
        await BulkSeedAsync(db, backlog, true);
        TestContext.Progress.WriteLine($"Seeded {count:N0} main rows and {backlog:N0} expired temporary rows in {timer.Elapsed}.");
        var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetReader>();
        var owners = Enumerable.Range(1, 50).ToArray();
        timer.Restart();
        var summaries = await reader.GetSummariesAsync<Order, int>(owners, OrderAssets.Attachments);
        summaries.Count.Should().Be(50);
        summaries.Values.Should().OnlyContain(x => x.Count == count / 1000 && x.First != null);
        TestContext.Progress.WriteLine($"50 owner summaries: {timer.Elapsed.TotalMilliseconds:F0} ms; result rows: {summaries.Count}.");
        db.ChangeTracker.Entries<ObjectAsset>().Should().BeEmpty();
        timer.Restart();
        var page = await scope.ServiceProvider.GetRequiredService<IObjectAssetPagedReader>()
            .GetOwnerPageAsync(new Order { Id = 1 }, OrderAssets.Attachments, new(25));
        page.Items.Count.Should().Be(Math.Min(25, count / 1000));
        TestContext.Progress.WriteLine($"Owner page (25 maximum): {timer.Elapsed.TotalMilliseconds:F0} ms.");
        timer.Restart();
        (await scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>().ExpireAssetsAsync()).Should().Be(Math.Min(batch, backlog));
        (await db.PermanentAssets.CountAsync()).Should().Be(count);
        (await db.TemporaryAssets.CountAsync()).Should().Be(backlog - Math.Min(batch, backlog));
        (await db.ArchivedAssets.CountAsync()).Should().Be(Math.Min(batch, backlog));
        db.ChangeTracker.Entries<ObjectAsset>().Count().Should().BeLessThanOrEqualTo(batch);
        TestContext.Progress.WriteLine($"Expiry batch ({batch} maximum): {timer.Elapsed.TotalMilliseconds:F0} ms; tracked payloads: {db.ChangeTracker.Entries<ObjectAsset>().Count()}.");
    }

    private static async Task BulkSeedAsync(ObjectStorageAssetDbContext db, int count, bool temporary)
    {
        var entity = db.Model.FindEntityType(temporary ? typeof(TemporaryObjectAsset) : typeof(PermanentObjectAsset))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var properties = entity.GetProperties().Where(p => p.PropertyInfo != null && p.GetComputedColumnSql() is null
            && p.Name != nameof(ObjectAsset.RowVersion)).ToArray();
        using var payload = new DataTable();
        foreach (var property in properties)
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            payload.Columns.Add(property.GetColumnName(store)!, type.IsEnum ? Enum.GetUnderlyingType(type) : type);
        }
        using var identities = new DataTable();
        identities.Columns.Add("AssetId", typeof(Guid)); identities.Columns.Add("Location", typeof(int));
        identities.Columns.Add("StorageAuthorityId", typeof(Guid)); identities.Columns.Add("BucketName", typeof(string));
        identities.Columns.Add("ObjectKey", typeof(string));
        await using var connection = new SqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        using var bulk = new SqlBulkCopy(connection) { DestinationTableName = $"[{entity.GetSchema()}].[{entity.GetTableName()}]", BulkCopyTimeout = 120 };
        using var identityBulk = new SqlBulkCopy(connection) { DestinationTableName = $"[{entity.GetSchema()}].[ObjectAssetIdentity]", BulkCopyTimeout = 120 };
        foreach (DataColumn column in payload.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        foreach (DataColumn column in identities.Columns) identityBulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        for (var i = 0; i < count; i++)
        {
            var asset = ObjectAsset.CreatePendingUpload(Guid.NewGuid(), "order", "attachments", ObjectAssetSlotMultiplicity.Many,
                "scale", "scale", $"scale/{Guid.NewGuid():N}.txt", "scale.txt", ".txt", "text/plain", created, 1, new string('a', 64));
            if (temporary) asset.BindTemporary("order", "attachments", ObjectAssetSlotMultiplicity.Many, Guid.NewGuid(), created.AddHours(1));
            else asset.BindOwner((long)(i % 1000 + 1));
            asset.MarkUploadSucceeded(created, null, null);
            payload.Rows.Add(properties.Select(p =>
            {
                var value = p.PropertyInfo!.GetValue(asset);
                return value is null ? DBNull.Value : value is Enum ? Convert.ToInt32(value) : value;
            }).ToArray());
            identities.Rows.Add(asset.Id, (int)(temporary ? ObjectAssetLocation.Temporary : ObjectAssetLocation.Main), Guid.Empty, asset.BucketName, asset.ObjectKey);
            if (payload.Rows.Count == 5000 || i == count - 1)
            {
                await identityBulk.WriteToServerAsync(identities);
                await bulk.WriteToServerAsync(payload);
                payload.Clear(); identities.Clear();
            }
        }
    }
}
