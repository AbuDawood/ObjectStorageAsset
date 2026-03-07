using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Elf.ObjectStorageAsset.TestSupport.TestDoubles;

namespace Elf.ObjectStorageAsset.InMemoryE2E;

public static class Program
{
    public static async Task<int> Main()
    {
        var settings = InMemoryE2ESettings.FromEnvironment();
        ServiceProvider? rootProvider = null;

        try
        {
            Console.WriteLine($"SQL Server: {settings.SqlServer}");
            Console.WriteLine($"Database: {settings.DatabaseName}");
            Console.WriteLine($"Bucket: {settings.BucketName}");

            var services = new ServiceCollection();
            services.AddObjectStorageAsset(options =>
            {
                options.ConnectionString = settings.ConnectionString;
                options.Schema = "osa";
                options.DefaultStorageNamespace = "inmemory-e2e";
                options.PhysicallyDeleteRequestedAssets = true;
                options.PhysicallyDeleteExpiredAssets = true;
                options.AddBinding<OrderAssets>();
            });
            services.AddSingleton<InMemoryMinioLikeObjectStorageProvider>();
            services.AddSingleton<IObjectStorageProvider>(provider =>
                provider.GetRequiredService<InMemoryMinioLikeObjectStorageProvider>());
            services.AddSingleton<IObjectStorageBucketResolver>(new FixedBucketResolver(settings.BucketName));

            rootProvider = services.BuildServiceProvider(validateScopes: true);

            using var scope = rootProvider.CreateScope();
            var storageProvider = scope.ServiceProvider.GetRequiredService<InMemoryMinioLikeObjectStorageProvider>();
            var osaDbContext = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
            await osaDbContext.Database.MigrateAsync().ConfigureAwait(false);
            await CreateOrdersTableAsync(osaDbContext).ConfigureAwait(false);

            await using var hostDbContext = CreateHostDbContext(settings.ConnectionString);
            var sessionFactory = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
            var coordinator = scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>();
            var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetReader>();
            var contentReader = scope.ServiceProvider.GetRequiredService<IObjectAssetContentReader>();
            var maintenanceService = scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>();
            var bindingCatalog = scope.ServiceProvider.GetRequiredService<IObjectAssetBindingCatalog>();

            var order = new Order
            {
                Number = $"ORD-INMEM-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            };
            hostDbContext.Orders.Add(order);

            var session = sessionFactory.For(order);
            await session.SetSingleAsync(
                    OrderAssets.Invoice,
                    CreateStream("inmemory-invoice"),
                    "inmemory-invoice.pdf",
                    "application/pdf")
                .ConfigureAwait(false);
            await session.AddAsync(
                    OrderAssets.Attachments,
                    CreateStream("attachment-a"),
                    "attachment-a.txt",
                    "text/plain")
                .ConfigureAwait(false);
            await session.AddAsync(
                    OrderAssets.Attachments,
                    CreateStream("attachment-b"),
                    "attachment-b.txt",
                    "text/plain")
                .ConfigureAwait(false);

            await coordinator.SaveChangesWithAssetsAsync(hostDbContext).ConfigureAwait(false);

            if (storageProvider.ObjectKeys.Count != 3)
            {
                throw new InvalidOperationException(
                    $"In-memory E2E expected 3 stored objects but found {storageProvider.ObjectKeys.Count}.");
            }

            var invoice = await reader.GetSingleAsync(order, OrderAssets.Invoice).ConfigureAwait(false)
                ?? throw new InvalidOperationException("In-memory E2E invoice was not created.");
            var attachments = await reader.GetManyAsync(order, OrderAssets.Attachments).ConfigureAwait(false);
            if (attachments.Count != 2)
            {
                throw new InvalidOperationException(
                    $"In-memory E2E expected 2 attachments but found {attachments.Count}.");
            }

            var content = await contentReader.OpenReadAsync(invoice.AssetId).ConfigureAwait(false)
                ?? throw new InvalidOperationException("In-memory E2E invoice content could not be opened.");
            await using var assetContent = content.Content;
            using (var streamReader = new StreamReader(assetContent, Encoding.UTF8))
            {
                var text = await streamReader.ReadToEndAsync().ConfigureAwait(false);
                if (!string.Equals(text, "inmemory-invoice", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"In-memory E2E invoice content mismatch. Actual '{text}'.");
                }
            }

            var invoiceRefs = hostDbContext.ObjectAssetSingleRefsFor<Order, int>(bindingCatalog, OrderAssets.Invoice);
            var attachmentSummaries = hostDbContext.ObjectAssetSlotSummariesFor<Order, int>(bindingCatalog, OrderAssets.Attachments);
            var projection = await hostDbContext.Orders
                .Where(x => x.Id == order.Id)
                .Select(x => new
                {
                    x.Id,
                    x.Number,
                    InvoiceFile = invoiceRefs
                        .Where(y => y.OwnerKey == x.Id)
                        .Select(y => y.FileName)
                        .FirstOrDefault(),
                    AttachmentCount = attachmentSummaries
                        .Where(y => y.OwnerKey == x.Id)
                        .Select(y => (int?)y.Count)
                        .FirstOrDefault() ?? 0
                })
                .SingleAsync()
                .ConfigureAwait(false);

            if (!string.Equals(projection.InvoiceFile, "inmemory-invoice.pdf", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"In-memory E2E query bridge invoice mismatch. Actual '{projection.InvoiceFile}'.");
            }

            if (projection.AttachmentCount != 2)
            {
                throw new InvalidOperationException(
                    $"In-memory E2E query bridge attachment count mismatch. Actual '{projection.AttachmentCount}'.");
            }

            await sessionFactory.For(order).RemoveAsync(invoice.AssetId).ConfigureAwait(false);
            foreach (var attachment in attachments)
            {
                await sessionFactory.For(order).RemoveAsync(attachment.AssetId).ConfigureAwait(false);
            }

            await coordinator.SaveChangesWithAssetsAsync(hostDbContext).ConfigureAwait(false);

            var remainingInvoice = await reader.GetSingleAsync(order, OrderAssets.Invoice).ConfigureAwait(false);
            var remainingAttachments = await reader.GetManyAsync(order, OrderAssets.Attachments).ConfigureAwait(false);
            if (remainingInvoice is not null || remainingAttachments.Count != 0)
            {
                throw new InvalidOperationException("In-memory E2E delete workflow did not remove active assets.");
            }

            if (storageProvider.ObjectKeys.Count != 0)
            {
                throw new InvalidOperationException(
                    $"In-memory E2E expected empty object storage after delete but found {storageProvider.ObjectKeys.Count} objects.");
            }

            var reconcileReport = await maintenanceService.ReconcileAsync().ConfigureAwait(false);
            if (reconcileReport.MissingActiveAssetIds.Count != 0)
            {
                throw new InvalidOperationException(
                    $"In-memory E2E reconciliation found missing active assets: {string.Join(", ", reconcileReport.MissingActiveAssetIds)}");
            }

            Console.WriteLine($"Created order id: {order.Id}");
            Console.WriteLine($"Invoice asset id: {invoice.AssetId}");
            Console.WriteLine($"Attachment count: {attachments.Count}");
            Console.WriteLine("In-memory E2E run completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("In-memory E2E run failed.");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (rootProvider is not null)
            {
                await rootProvider.DisposeAsync().ConfigureAwait(false);
            }

            SqlConnection.ClearAllPools();
            await CleanupDatabaseAsync(settings).ConfigureAwait(false);
        }
    }

    private static async Task CleanupDatabaseAsync(InMemoryE2ESettings settings)
    {
        var options = new DbContextOptionsBuilder<ObjectStorageAssetDbContext>()
            .UseSqlServer(settings.ConnectionString)
            .Options;

        await using var dbContext = new ObjectStorageAssetDbContext(
            options,
            new ObjectStorageAssetSchema("osa"));

        try
        {
            await dbContext.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup for in-memory E2E artifacts.
        }
    }

    private static async Task CreateOrdersTableAsync(ObjectStorageAssetDbContext dbContext)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[dbo].[Orders]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[Orders] (
                    [Id] INT NOT NULL IDENTITY,
                    [Number] NVARCHAR(256) NOT NULL,
                    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
                );
            END
            """).ConfigureAwait(false);
    }

    private static InMemoryE2EHostDbContext CreateHostDbContext(string connectionString)
    {
        return new InMemoryE2EHostDbContext(
            new DbContextOptionsBuilder<InMemoryE2EHostDbContext>()
                .UseSqlServer(connectionString)
                .Options);
    }

    private static MemoryStream CreateStream(string content)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    private sealed class InMemoryE2EHostDbContext(DbContextOptions<InMemoryE2EHostDbContext> options) : DbContext(options)
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

    private sealed record InMemoryE2ESettings(
        string SqlServer,
        string SqlUser,
        string SqlPassword,
        string DatabaseName,
        string BucketName)
    {
        public string ConnectionString =>
            $"Server={SqlServer};Database={DatabaseName};User ID={SqlUser};Password={SqlPassword};TrustServerCertificate=True;";

        public static InMemoryE2ESettings FromEnvironment()
        {
            var databaseName = ReadSetting("OSA_INMEMORY_E2E_SQL_DATABASE", "OSA_E2E_SQL_DATABASE");
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                databaseName = $"ObjectStorageAsset_InMemoryE2E_{Guid.NewGuid():N}";
            }

            return new InMemoryE2ESettings(
                ReadSetting("OSA_INMEMORY_E2E_SQL_SERVER", "OSA_E2E_SQL_SERVER") ?? "10.0.2.2,1433",
                ReadSetting("OSA_INMEMORY_E2E_SQL_USER", "OSA_E2E_SQL_USER") ?? "sa",
                ReadSetting("OSA_INMEMORY_E2E_SQL_PASSWORD", "OSA_E2E_SQL_PASSWORD") ?? "Password@123",
                databaseName,
                ReadSetting("OSA_INMEMORY_E2E_BUCKET", "OSA_E2E_BUCKET") ?? "osa-inmemory-e2e");
        }

        private static string? ReadSetting(string primaryKey, string fallbackKey)
        {
            return Environment.GetEnvironmentVariable(primaryKey)
                ?? Environment.GetEnvironmentVariable(fallbackKey);
        }
    }
}
