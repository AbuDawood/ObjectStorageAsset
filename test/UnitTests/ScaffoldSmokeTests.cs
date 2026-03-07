using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Elf.ObjectStorageAsset;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Minio;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class ScaffoldSmokeTests
{
    [Test]
    public void AddObjectStorageAsset_ShouldRegisterOptionsAndBindingCatalog()
    {
        var services = new ServiceCollection();

        services.AddObjectStorageAsset(options =>
        {
            options.ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=ObjectStorageAsset;";
            options.Schema = "osa";
            options.DefaultStorageNamespace = "unit-tests";
            options.AddBinding<OrderAssets>();
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Elf.ObjectStorageAsset.ObjectStorageAssetOptions>();
        var catalog = provider.GetRequiredService<IObjectAssetBindingCatalog>();

        options.Schema.Should().Be("osa");
        catalog.GetOwner<Order>().OwnerType.Should().Be("order");
        catalog.GetOwner<Order>().Slots.Select(x => x.Name).Should().Equal("invoice", "attachments");
    }

    [Test]
    public void AddObjectStorageAssetMinio_ShouldRegisterOptionsInstance()
    {
        var services = new ServiceCollection();

        services.AddObjectStorageAssetMinio(options =>
        {
            options.Endpoint = "localhost:9000";
            options.AccessKey = "minioadmin";
            options.SecretKey = "minioadmin";
            options.BucketName = "osa-dev";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Elf.ObjectStorageAsset.Minio.MinioObjectStorageOptions>();

        options.BucketName.Should().Be("osa-dev");
    }

    [Test]
    public void AddObjectStorageAsset_ShouldRejectDuplicateSlotsWithinOneOwner()
    {
        var services = new ServiceCollection();

        var act = () => services.AddObjectStorageAsset(options =>
        {
            options.ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=ObjectStorageAsset;";
            options.DefaultStorageNamespace = "unit-tests";
            options.AddBinding<DuplicateSlotAssets>();
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicated*");
    }

    private sealed class Order
    {
        public int Id { get; set; }
    }

    private sealed class OrderAssets : IObjectAssetBinding<Order, int>
    {
        public void Configure(IObjectAssetOwnerBuilder<Order, int> builder)
        {
            builder.OwnerType("order");
            builder.Key(x => x.Id);
            builder.Slot(ObjectAssetSlot.Single("invoice"));
            builder.Slot(ObjectAssetSlot.Many("attachments"));
        }
    }

    private sealed class DuplicateSlotAssets : IObjectAssetBinding<Order, int>
    {
        public void Configure(IObjectAssetOwnerBuilder<Order, int> builder)
        {
            builder.OwnerType("order-duplicate");
            builder.Key(x => x.Id);
            builder.Slot(ObjectAssetSlot.Single("invoice"));
            builder.Slot(ObjectAssetSlot.Many("invoice"));
        }
    }
}
