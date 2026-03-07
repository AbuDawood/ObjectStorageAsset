using Microsoft.Extensions.DependencyInjection;
using Elf.ObjectStorageAsset;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Minio;

namespace Elf.ObjectStorageAsset.ConsoleTest;

public static class Program
{
    public static void Main()
    {
        var services = new ServiceCollection();
        services.AddObjectStorageAsset(options =>
        {
            options.ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=ObjectStorageAsset;";
            options.Schema = "osa";
            options.DefaultStorageNamespace = "console-sample";
            options.AddBinding<OrderAssets>();
        });
        services.AddObjectStorageAssetMinio(options =>
        {
            options.Endpoint = "localhost:9000";
            options.AccessKey = "minioadmin";
            options.SecretKey = "minioadmin";
            options.BucketName = "osa-dev";
        });

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IObjectAssetBindingCatalog>();
        var keyStrategy = provider.GetRequiredService<IObjectKeyStrategy>();
        var storageProvider = provider.GetRequiredService<IObjectStorageProvider>();
        using var scope = provider.CreateScope();
        var sessionFactory = scope.ServiceProvider.GetRequiredService<IObjectAssetSessionFactory>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IObjectAssetCoordinator>();
        var reader = scope.ServiceProvider.GetRequiredService<IObjectAssetReader>();
        var contentReader = scope.ServiceProvider.GetRequiredService<IObjectAssetContentReader>();
        var maintenanceService = scope.ServiceProvider.GetRequiredService<IObjectAssetMaintenanceService>();
        var sampleKey = keyStrategy.CreateObjectKey(new ObjectKeyContext(
            Guid.Parse("cdb0a83f-b870-4e87-9f0e-42d4d53a6305"),
            "console-sample",
            "invoice.pdf",
            ".pdf",
            DateTimeOffset.Parse("2026-03-06T00:00:00Z")));

        Console.WriteLine(
            $"Elf.ObjectStorageAsset console scaffold is ready with {catalog.GetOwners().Count} binding(s), provider {storageProvider.GetType().Name}, workflow services [{sessionFactory.GetType().Name}, {coordinator.GetType().Name}, {reader.GetType().Name}, {contentReader.GetType().Name}, {maintenanceService.GetType().Name}], sample key '{sampleKey}'.");
    }

    private sealed class Order
    {
        public int Id { get; set; }
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
