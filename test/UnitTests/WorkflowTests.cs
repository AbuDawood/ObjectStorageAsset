using System.Text;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Elf.ObjectStorageAsset.TestSupport.TestDoubles;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class WorkflowTests
{
    [Test]
    public async Task SaveChangesWithAssetsAsync_ShouldPersistAndReadSingleAsset()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var hostDbContext = fixture.CreateHostDbContext();
        using var scope = fixture.RootProvider.CreateScope();

        var sessionFactory = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>();
        var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetReader>();
        var contentReader = scope.ServiceProvider.GetRequiredService<IObjectAssetContentReader>();

        var order = new Order { Number = "ORD-001" };
        hostDbContext.Orders.Add(order);

        var session = sessionFactory.For(order);
        await session.SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("invoice-v1"),
            "invoice-v1.pdf",
            "application/pdf");

        await coordinator.SaveChangesWithAssetsAsync(hostDbContext);

        order.Id.Should().BeGreaterThan(0);
        fixture.StorageProvider.ObjectKeys.Should().HaveCount(1);

        var invoice = await reader.GetSingleAsync(order, OrderAssets.Invoice);
        invoice.Should().NotBeNull();
        invoice!.FileName.Should().Be("invoice-v1.pdf");

        var content = await contentReader.OpenReadAsync(invoice.AssetId);
        content.Should().NotBeNull();
        content!.ContentType.Should().Be("application/pdf");
        await using var assetContent = content.Content;
        using var readerStream = new StreamReader(assetContent, Encoding.UTF8);
        (await readerStream.ReadToEndAsync()).Should().Be("invoice-v1");
    }

    [Test]
    public async Task SaveChangesWithAssetsAsync_ShouldReplaceSingleSlotAndDeletePreviousObject()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var hostDbContext = fixture.CreateHostDbContext();
        using var scope = fixture.RootProvider.CreateScope();

        var sessionFactory = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>();
        var osaDbContext = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();

        var order = new Order { Number = "ORD-002" };
        hostDbContext.Orders.Add(order);

        await sessionFactory.For(order).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("invoice-v1"),
            "invoice-v1.pdf",
            "application/pdf");
        await coordinator.SaveChangesWithAssetsAsync(hostDbContext);

        var firstAsset = await osaDbContext.ObjectAssets
            .AsNoTracking()
            .SingleAsync();

        await sessionFactory.For(order).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("invoice-v2"),
            "invoice-v2.pdf",
            "application/pdf");
        await coordinator.SaveChangesWithAssetsAsync(hostDbContext);

        var assets = await osaDbContext.ObjectAssets
            .AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .ToArrayAsync();

        assets.Should().HaveCount(2);
        assets.Count(x => x.Status == ObjectAssetStatus.Active).Should().Be(1);
        assets.Count(x => x.Status == ObjectAssetStatus.Deleted).Should().Be(1);
        fixture.StorageProvider.ContainsObject(firstAsset.ObjectKey).Should().BeFalse();
        fixture.StorageProvider.ObjectKeys.Should().HaveCount(1);
        assets.Single(x => x.Status == ObjectAssetStatus.Active).OriginalFileName.Should().Be("invoice-v2.pdf");
    }

    [Test]
    public async Task ObjectAssetReader_ShouldReturnBatchSinglesManyAndSummaries()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var hostDbContext = fixture.CreateHostDbContext();
        using var scope = fixture.RootProvider.CreateScope();

        var sessionFactory = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>();
        var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetReader>();

        var orderOne = new Order { Number = "ORD-101" };
        var orderTwo = new Order { Number = "ORD-102" };
        hostDbContext.Orders.AddRange(orderOne, orderTwo);

        await sessionFactory.For(orderOne).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("invoice-1"),
            "invoice-1.pdf",
            "application/pdf");
        await sessionFactory.For(orderOne).AddAsync(
            OrderAssets.Attachments,
            CreateStream("attachment-a"),
            "attachment-a.txt",
            "text/plain");
        await sessionFactory.For(orderOne).AddAsync(
            OrderAssets.Attachments,
            CreateStream("attachment-b"),
            "attachment-b.txt",
            "text/plain");

        await sessionFactory.For(orderTwo).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("invoice-2"),
            "invoice-2.pdf",
            "application/pdf");
        await sessionFactory.For(orderTwo).AddAsync(
            OrderAssets.Attachments,
            CreateStream("attachment-c"),
            "attachment-c.txt",
            "text/plain");

        await coordinator.SaveChangesWithAssetsAsync(hostDbContext);

        var ownerKeys = new[] { orderOne.Id, orderTwo.Id };
        var invoices = await reader.GetSinglesAsync<Order, int>(ownerKeys, OrderAssets.Invoice);
        var attachments = await reader.GetManyByOwnersAsync<Order, int>(ownerKeys, OrderAssets.Attachments);
        var summaries = await reader.GetSummariesAsync<Order, int>(ownerKeys, OrderAssets.Attachments);

        invoices.Should().HaveCount(2);
        invoices[orderOne.Id].FileName.Should().Be("invoice-1.pdf");
        invoices[orderTwo.Id].FileName.Should().Be("invoice-2.pdf");

        attachments[orderOne.Id].Should().HaveCount(2);
        attachments[orderTwo.Id].Should().HaveCount(1);
        summaries[orderOne.Id].Count.Should().Be(2);
        summaries[orderTwo.Id].Count.Should().Be(1);
        summaries[orderOne.Id].First!.FileName.Should().Be("attachment-b.txt");
    }

    [Test]
    public async Task QueryBridge_ShouldJoinOrdersWithInvoiceAndAttachmentSummary()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var hostDbContext = fixture.CreateHostDbContext();
        using var scope = fixture.RootProvider.CreateScope();

        var sessionFactory = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>();
        var bindingCatalog = scope.ServiceProvider.GetRequiredService<IObjectAssetBindingCatalog>();

        var orderOne = new Order { Number = "ORD-201" };
        var orderTwo = new Order { Number = "ORD-202" };
        hostDbContext.Orders.AddRange(orderOne, orderTwo);

        await sessionFactory.For(orderOne).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("invoice-1"),
            "invoice-1.pdf",
            "application/pdf");
        await sessionFactory.For(orderOne).AddAsync(
            OrderAssets.Attachments,
            CreateStream("attachment-a"),
            "attachment-a.txt",
            "text/plain");
        await sessionFactory.For(orderOne).AddAsync(
            OrderAssets.Attachments,
            CreateStream("attachment-b"),
            "attachment-b.txt",
            "text/plain");
        await sessionFactory.For(orderTwo).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("invoice-2"),
            "invoice-2.pdf",
            "application/pdf");

        await coordinator.SaveChangesWithAssetsAsync(hostDbContext);

        var invoiceRefs = hostDbContext.ObjectAssetSingleRefsFor<Order, int>(bindingCatalog, OrderAssets.Invoice);
        var attachmentSummaries = hostDbContext.ObjectAssetSlotSummariesFor<Order, int>(bindingCatalog, OrderAssets.Attachments);

        var query = hostDbContext.Orders
            .OrderBy(order => order.Id)
            .Select(order => new
            {
                order.Number,
                InvoiceFile = invoiceRefs
                    .Where(invoice => invoice.OwnerKey == order.Id)
                    .Select(invoice => invoice.FileName)
                    .FirstOrDefault(),
                AttachmentCount = attachmentSummaries
                    .Where(summary => summary.OwnerKey == order.Id)
                    .Select(summary => (int?)summary.Count)
                    .FirstOrDefault() ?? 0
            });

        var rows = await query.ToArrayAsync();

        rows.Should().HaveCount(2);
        rows[0].InvoiceFile.Should().Be("invoice-1.pdf");
        rows[0].AttachmentCount.Should().Be(2);
        rows[1].InvoiceFile.Should().Be("invoice-2.pdf");
        rows[1].AttachmentCount.Should().Be(0);
    }

    [Test]
    public async Task ExpireAssetsAsync_ShouldPhysicallyDeleteExpiredAssets_WhenConfigured()
    {
        await using var fixture = await SqlServerFixture.CreateAsync(physicallyDeleteExpiredAssets: true);
        await using var hostDbContext = fixture.CreateHostDbContext();
        using var scope = fixture.RootProvider.CreateScope();

        var sessionFactory = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>();
        var maintenanceService = scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>();
        var osaDbContext = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();

        var order = new Order { Number = "ORD-301" };
        hostDbContext.Orders.Add(order);

        await sessionFactory.For(order).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("invoice-expired"),
            "invoice-expired.pdf",
            "application/pdf",
            DateTimeOffset.UtcNow.AddMinutes(-5));
        await coordinator.SaveChangesWithAssetsAsync(hostDbContext);

        var expiredCount = await maintenanceService.ExpireAssetsAsync(DateTimeOffset.UtcNow);
        var asset = await osaDbContext.ObjectAssets.AsNoTracking().SingleAsync();

        expiredCount.Should().Be(1);
        asset.Status.Should().Be(ObjectAssetStatus.Deleted);
        asset.PhysicalDeletedAtUtc.Should().NotBeNull();
        fixture.StorageProvider.ObjectKeys.Should().BeEmpty();
    }

    [Test]
    public async Task SaveChangesWithAssetsAsync_ShouldMarkUploadFailure_WhenProviderUploadFails()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var hostDbContext = fixture.CreateHostDbContext();
        using var scope = fixture.RootProvider.CreateScope();

        var sessionFactory = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>();
        var osaDbContext = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();

        fixture.StorageProvider.ThrowOnPut = true;

        var order = new Order { Number = "ORD-401" };
        hostDbContext.Orders.Add(order);
        await sessionFactory.For(order).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("broken-upload"),
            "broken-upload.pdf",
            "application/pdf");

        var act = async () => await coordinator.SaveChangesWithAssetsAsync(hostDbContext);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*put failed*");

        var asset = await osaDbContext.ObjectAssets.AsNoTracking().SingleAsync();
        asset.Status.Should().Be(ObjectAssetStatus.UploadFailed);
        fixture.StorageProvider.ObjectKeys.Should().BeEmpty();
    }

    [Test]
    public async Task MaintenanceService_ShouldReconcileMissingActiveAssets_AndRetryDeleteFailures()
    {
        await using var fixture = await SqlServerFixture.CreateAsync();
        await using var hostDbContext = fixture.CreateHostDbContext();
        using var scope = fixture.RootProvider.CreateScope();

        var sessionFactory = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>();
        var maintenanceService = scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>();
        var osaDbContext = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();

        var activeOrder = new Order { Number = "ORD-501" };
        var deleteOrder = new Order { Number = "ORD-502" };
        hostDbContext.Orders.AddRange(activeOrder, deleteOrder);

        await sessionFactory.For(activeOrder).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("active"),
            "active.pdf",
            "application/pdf");
        await sessionFactory.For(deleteOrder).SetSingleAsync(
            OrderAssets.Invoice,
            CreateStream("to-delete"),
            "to-delete.pdf",
            "application/pdf");
        await coordinator.SaveChangesWithAssetsAsync(hostDbContext);

        var assets = await osaDbContext.ObjectAssets
            .AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .ToArrayAsync();
        var missingActiveAsset = assets.Single(x => x.OwnerKeyInt64 == activeOrder.Id);
        var deleteAsset = assets.Single(x => x.OwnerKeyInt64 == deleteOrder.Id);

        fixture.StorageProvider.RemoveObject(missingActiveAsset.ObjectKey);
        fixture.StorageProvider.ThrowOnDelete = true;

        await sessionFactory.For(deleteOrder).RemoveAsync(deleteAsset.Id);

        var deleteAct = async () => await coordinator.SaveChangesWithAssetsAsync(hostDbContext);
        await deleteAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*delete failed*");

        var report = await maintenanceService.ReconcileAsync();
        report.MissingActiveAssetIds.Should().Contain(missingActiveAsset.Id);
        report.DeleteFailedAssetIds.Should().Contain(deleteAsset.Id);

        fixture.StorageProvider.ThrowOnDelete = false;
        var retriedCount = await maintenanceService.RetryDeleteFailuresAsync();
        retriedCount.Should().Be(1);

        var updatedDeleteAsset = await osaDbContext.ObjectAssets
            .AsNoTracking()
            .SingleAsync(x => x.Id == deleteAsset.Id);
        updatedDeleteAsset.Status.Should().Be(ObjectAssetStatus.Deleted);
        updatedDeleteAsset.PhysicalDeletedAtUtc.Should().NotBeNull();
    }

    private static MemoryStream CreateStream(string content)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    private sealed class SqlServerFixture : IAsyncDisposable
    {
        private SqlServerFixture(
            string connectionString,
            string databaseName,
            ServiceProvider rootProvider,
            InMemoryMinioLikeObjectStorageProvider storageProvider)
        {
            ConnectionString = connectionString;
            DatabaseName = databaseName;
            RootProvider = rootProvider;
            StorageProvider = storageProvider;
        }

        public string ConnectionString { get; }

        public string DatabaseName { get; }

        public ServiceProvider RootProvider { get; }

        public InMemoryMinioLikeObjectStorageProvider StorageProvider { get; }

        public static async Task<SqlServerFixture> CreateAsync(
            bool physicallyDeleteRequestedAssets = true,
            bool physicallyDeleteExpiredAssets = true)
        {
            var databaseName = $"ObjectStorageAssetWorkflow_{Guid.NewGuid():N}";
            var connectionString = BuildConnectionString(databaseName);
            var storageProvider = new InMemoryMinioLikeObjectStorageProvider();
            var services = new ServiceCollection();
            services.AddObjectStorageAsset(options =>
            {
                options.ConnectionString = connectionString;
                options.Schema = "osa";
                options.DefaultStorageNamespace = "unit-tests";
                options.PhysicallyDeleteRequestedAssets = physicallyDeleteRequestedAssets;
                options.PhysicallyDeleteExpiredAssets = physicallyDeleteExpiredAssets;
                options.AddBinding<OrderAssets>();
            });

            services.RemoveAll<DbContextOptions<ObjectStorageAssetDbContext>>();
            services.RemoveAll<ObjectStorageAssetDbContext>();
            services.AddDbContext<ObjectStorageAssetDbContext>((_, builder) =>
                builder.UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        ObjectStorageAssetDbContext.MigrationsHistoryTableName,
                        "osa")));
            services.AddSingleton<IObjectStorageProvider>(storageProvider);
            services.AddSingleton<IObjectStorageBucketResolver>(new FixedBucketResolver("osa-dev"));

            var rootProvider = services.BuildServiceProvider(validateScopes: true);
            using (var scope = rootProvider.CreateScope())
            {
                var osaDbContext = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
                await osaDbContext.Database.EnsureDeletedAsync();
                await osaDbContext.Database.EnsureCreatedAsync();
                await CreateOrdersTableAsync(osaDbContext);
            }

            return new SqlServerFixture(connectionString, databaseName, rootProvider, storageProvider);
        }

        public HostDbContext CreateHostDbContext()
        {
            return new HostDbContext(
                new DbContextOptionsBuilder<HostDbContext>()
                    .UseSqlServer(ConnectionString)
                    .Options);
        }

        public async ValueTask DisposeAsync()
        {
            await RootProvider.DisposeAsync();

            SqlConnection.ClearAllPools();

            var options = new DbContextOptionsBuilder<ObjectStorageAssetDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            await using var cleanupDbContext = new ObjectStorageAssetDbContext(
                options,
                new ObjectStorageAssetSchema("osa"));
            await cleanupDbContext.Database.EnsureDeletedAsync();
        }

        private static Task CreateOrdersTableAsync(ObjectStorageAssetDbContext dbContext)
        {
            return dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[dbo].[Orders]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[Orders] (
                        [Id] INT NOT NULL IDENTITY,
                        [Number] NVARCHAR(256) NOT NULL,
                        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
                    );
                END
                """);
        }

        private static string BuildConnectionString(string databaseName)
        {
            var server = Environment.GetEnvironmentVariable("OSA_TEST_SQL_SERVER") ?? "10.0.2.2,1433";
            var userId = Environment.GetEnvironmentVariable("OSA_TEST_SQL_USER") ?? "sa";
            var password = Environment.GetEnvironmentVariable("OSA_TEST_SQL_PASSWORD") ?? "Password@123";

            return $"Server={server};Database={databaseName};User ID={userId};Password={password};TrustServerCertificate=True;";
        }
    }

    private sealed class HostDbContext(DbContextOptions<HostDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("Orders");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.Number).IsRequired();
            });

            modelBuilder.ApplyObjectStorageAssetQueryModel("osa");

            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class Order
    {
        public int Id { get; set; }

        public string Number { get; set; } = string.Empty;
    }

    private sealed class OrderAssets : IObjectAssetBinding<Order, int>
    {
        public static readonly ObjectAssetSlot Invoice = ObjectAssetSlot.Single("invoice");
        public static readonly ObjectAssetSlot Attachments = ObjectAssetSlot.Many("attachments");

        public void Configure(IObjectAssetOwnerBuilder<Order, int> builder)
        {
            builder.OwnerType("order");
            builder.Key(x => x.Id);
            builder.Slot(Invoice);
            builder.Slot(Attachments);
        }
    }
}
