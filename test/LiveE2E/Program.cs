using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Elf.ObjectStorageAsset.Minio;

namespace Elf.ObjectStorageAsset.LiveE2E;

public static class Program
{
    public static async Task<int> Main()
    {
        var settings = LiveE2ESettings.FromEnvironment();
        ServiceProvider? rootProvider = null;

        try
        {
            Console.WriteLine($"SQL Server: {settings.SqlServer}");
            Console.WriteLine($"Database: {settings.DatabaseName}");
            Console.WriteLine($"MinIO endpoint: {settings.MinioEndpoint}");
            Console.WriteLine($"Bucket: {settings.BucketName}");

            var services = new ServiceCollection();
            services.AddObjectStorageAsset(options =>
            {
                options.ConnectionString = settings.ConnectionString;
                options.Schema = "osa";
                options.DefaultStorageNamespace = settings.StorageNamespace;
                options.PhysicallyDeleteRequestedAssets = true;
                options.PhysicallyDeleteExpiredAssets = true;
                options.AddBinding<OrderAssets>();
            });
            services.AddObjectStorageAssetMinio(options =>
            {
                options.Endpoint = settings.MinioEndpoint;
                options.AccessKey = settings.MinioAccessKey;
                options.SecretKey = settings.MinioSecretKey;
                options.BucketName = settings.BucketName;
                options.AutoCreateBucket = settings.MinioAutoCreateBucket;
                options.UseSsl = settings.MinioUseSsl;
            });

            rootProvider = services.BuildServiceProvider(validateScopes: true);

            Guid invoiceAssetId;
            Guid[] attachmentAssetIds;

            using (var scope = rootProvider.CreateScope())
            {
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
                    Number = $"ORD-LIVE-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
                };
                hostDbContext.Orders.Add(order);

                var session = sessionFactory.For(order);
                await session.SetSingleAsync(
                        OrderAssets.Invoice,
                        CreateStream("live-invoice"),
                        "live-invoice.pdf",
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

                var invoice = await reader.GetSingleAsync(order, OrderAssets.Invoice).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Live E2E invoice was not created.");
                var attachments = await reader.GetManyAsync(order, OrderAssets.Attachments).ConfigureAwait(false);
                if (attachments.Count != 2)
                {
                    throw new InvalidOperationException(
                        $"Live E2E expected 2 attachments but found {attachments.Count}.");
                }

                invoiceAssetId = invoice.AssetId;
                attachmentAssetIds = attachments.Select(x => x.AssetId).ToArray();

                var content = await contentReader.OpenReadAsync(invoice.AssetId).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Live E2E invoice content could not be opened.");
                await using var liveContent = content.Content;
                using (var streamReader = new StreamReader(liveContent, Encoding.UTF8))
                {
                    var contentText = await streamReader.ReadToEndAsync().ConfigureAwait(false);
                    if (!string.Equals(contentText, "live-invoice", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Live E2E invoice content mismatch. Actual '{contentText}'.");
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

                if (!string.Equals(projection.InvoiceFile, "live-invoice.pdf", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Live E2E query bridge invoice mismatch. Actual '{projection.InvoiceFile}'.");
                }

                if (projection.AttachmentCount != 2)
                {
                    throw new InvalidOperationException(
                        $"Live E2E query bridge attachment count mismatch. Actual '{projection.AttachmentCount}'.");
                }

                var reconcileReport = await maintenanceService.ReconcileAsync().ConfigureAwait(false);
                if (reconcileReport.MissingActiveAssetIds.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"Live E2E reconciliation found missing active assets: {string.Join(", ", reconcileReport.MissingActiveAssetIds)}");
                }

                if (!settings.SkipDeleteWorkflow)
                {
                    await sessionFactory.For(order).RemoveAsync(invoiceAssetId).ConfigureAwait(false);
                    foreach (var attachmentAssetId in attachmentAssetIds)
                    {
                        await sessionFactory.For(order).RemoveAsync(attachmentAssetId).ConfigureAwait(false);
                    }

                    await coordinator.SaveChangesWithAssetsAsync(hostDbContext).ConfigureAwait(false);

                    var remainingInvoice = await reader.GetSingleAsync(order, OrderAssets.Invoice).ConfigureAwait(false);
                    var remainingAttachments = await reader.GetManyAsync(order, OrderAssets.Attachments).ConfigureAwait(false);
                    if (remainingInvoice is not null || remainingAttachments.Count != 0)
                    {
                        throw new InvalidOperationException("Live E2E delete workflow did not remove active assets.");
                    }
                }
                else
                {
                    Console.WriteLine("Delete workflow skipped; active assets and provider objects were preserved.");
                }

                Console.WriteLine($"Created order id: {order.Id}");
                Console.WriteLine($"Invoice asset id: {invoiceAssetId}");
                Console.WriteLine($"Attachment asset ids: {string.Join(", ", attachmentAssetIds)}");
                Console.WriteLine($"Query bridge attachment count: {projection.AttachmentCount}");
                Console.WriteLine($"Storage namespace: {settings.StorageNamespace}");
                Console.WriteLine($"Preserve database: {settings.PreserveDatabase}");
                Console.WriteLine($"Preserve objects: {settings.PreserveObjects}");
                Console.WriteLine("Live E2E run completed successfully.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Live E2E run failed.");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (rootProvider is not null)
            {
                if (!settings.PreserveObjects)
                {
                    await CleanupObjectsAsync(rootProvider).ConfigureAwait(false);
                }

                await rootProvider.DisposeAsync().ConfigureAwait(false);
            }

            SqlConnection.ClearAllPools();
            if (!settings.PreserveDatabase)
            {
                await CleanupDatabaseAsync(settings).ConfigureAwait(false);
            }
        }
    }

    private static async Task CleanupObjectsAsync(ServiceProvider rootProvider)
    {
        using var scope = rootProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var objectStorageProvider = scope.ServiceProvider.GetRequiredService<IObjectStorageProvider>();

        var objectKeys = await dbContext.ObjectAssets
            .AsNoTracking()
            .Select(x => new { x.BucketName, x.ObjectKey, x.ProviderVersionId })
            .ToArrayAsync()
            .ConfigureAwait(false);

        foreach (var objectKey in objectKeys)
        {
            try
            {
                await objectStorageProvider.DeleteObjectAsync(
                        new ObjectStorageDeleteRequest
                        {
                            BucketName = objectKey.BucketName,
                            ObjectKey = objectKey.ObjectKey,
                            VersionId = objectKey.ProviderVersionId
                        })
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup for live E2E artifacts.
            }
        }
    }

    private static async Task CleanupDatabaseAsync(LiveE2ESettings settings)
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
            // Best-effort cleanup for live E2E artifacts.
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

    private static LiveE2EHostDbContext CreateHostDbContext(string connectionString)
    {
        return new LiveE2EHostDbContext(
            new DbContextOptionsBuilder<LiveE2EHostDbContext>()
                .UseSqlServer(connectionString)
                .Options);
    }

    private static MemoryStream CreateStream(string content)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    private sealed class LiveE2EHostDbContext(DbContextOptions<LiveE2EHostDbContext> options) : DbContext(options)
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

    private sealed record LiveE2ESettings(
        string SqlServer,
        string SqlUser,
        string SqlPassword,
        string DatabaseName,
        string StorageNamespace,
        string MinioEndpoint,
        string MinioAccessKey,
        string MinioSecretKey,
        string BucketName,
        bool MinioAutoCreateBucket,
        bool MinioUseSsl,
        bool PreserveDatabase,
        bool PreserveObjects,
        bool SkipDeleteWorkflow)
    {
        public string ConnectionString =>
            $"Server={SqlServer};Database={DatabaseName};User ID={SqlUser};Password={SqlPassword};TrustServerCertificate=True;";

        public static LiveE2ESettings FromEnvironment()
        {
            var databaseName = Environment.GetEnvironmentVariable("OSA_E2E_SQL_DATABASE");
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                databaseName = $"ObjectStorageAsset_LiveE2E_{Guid.NewGuid():N}";
            }

            return new LiveE2ESettings(
                Environment.GetEnvironmentVariable("OSA_E2E_SQL_SERVER") ?? "10.0.2.2,1433",
                Environment.GetEnvironmentVariable("OSA_E2E_SQL_USER") ?? "sa",
                Environment.GetEnvironmentVariable("OSA_E2E_SQL_PASSWORD") ?? "Password@123",
                databaseName,
                Environment.GetEnvironmentVariable("OSA_E2E_STORAGE_NAMESPACE") ?? "live-e2e",
                Environment.GetEnvironmentVariable("OSA_E2E_MINIO_ENDPOINT") ?? "10.0.2.2:19000",
                Environment.GetEnvironmentVariable("OSA_E2E_MINIO_ACCESS_KEY") ?? "minioadmin",
                Environment.GetEnvironmentVariable("OSA_E2E_MINIO_SECRET_KEY") ?? "minioadmin",
                Environment.GetEnvironmentVariable("OSA_E2E_BUCKET") ?? "osa-live-e2e",
                !bool.TryParse(Environment.GetEnvironmentVariable("OSA_E2E_MINIO_AUTO_CREATE_BUCKET"), out var autoCreateBucket) || autoCreateBucket,
                bool.TryParse(Environment.GetEnvironmentVariable("OSA_E2E_MINIO_USE_SSL"), out var useSsl) && useSsl,
                bool.TryParse(Environment.GetEnvironmentVariable("OSA_E2E_PRESERVE_DATABASE"), out var preserveDatabase) && preserveDatabase,
                bool.TryParse(Environment.GetEnvironmentVariable("OSA_E2E_PRESERVE_OBJECTS"), out var preserveObjects) && preserveObjects,
                bool.TryParse(Environment.GetEnvironmentVariable("OSA_E2E_SKIP_DELETE_WORKFLOW"), out var skipDeleteWorkflow) && skipDeleteWorkflow);
        }
    }
}
