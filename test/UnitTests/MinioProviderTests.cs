using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Response;
using Moq;
using Elf.ObjectStorageAsset;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure;
using Elf.ObjectStorageAsset.Minio;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class MinioProviderTests
{
    [Test]
    public void DefaultObjectKeyStrategy_ShouldComposeStablePath()
    {
        var strategy = new DefaultObjectKeyStrategy();

        var objectKey = strategy.CreateObjectKey(new ObjectKeyContext(
            Guid.Parse("a38b6f06-8f26-43da-a8d4-553c4fd7c1a1"),
            "UP Notification",
            "Invoice.PDF",
            ".PDF",
            DateTimeOffset.Parse("2026-03-06T11:22:33Z")));

        objectKey.Should().Be("up-notification/2026/03/a38b6f068f2643daa8d4553c4fd7c1a1.pdf");
    }

    [Test]
    public void ObjectStorageSystemMetadata_ShouldStampReservedKeys()
    {
        var metadata = ObjectStorageSystemMetadata.Create(
            Guid.Parse("0f0d92fd-4de6-4698-b1ec-561779497ba1"),
            "up-notification",
            "hash-value");

        metadata.Should().ContainKey(ObjectStorageSystemMetadata.AssetIdKey);
        metadata.Should().ContainKey(ObjectStorageSystemMetadata.StorageNamespaceKey);
        metadata.Should().ContainKey(ObjectStorageSystemMetadata.Sha256Key);
        metadata[ObjectStorageSystemMetadata.AssetIdKey].Should().Be("0f0d92fd4de64698b1ec561779497ba1");
    }

    [Test]
    public void AddObjectStorageAssetMinio_ShouldRegisterProvider()
    {
        var services = new ServiceCollection();

        services.AddObjectStorageAsset(options =>
        {
            options.ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=ObjectStorageAsset;";
            options.Schema = "osa";
            options.DefaultStorageNamespace = "up-notification";
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

        provider.GetRequiredService<IObjectStorageProvider>().Should().BeOfType<MinioObjectStorageProvider>();
        provider.GetRequiredService<IMinioClient>().Should().NotBeNull();
        provider.GetRequiredService<IObjectKeyStrategy>().Should().BeOfType<DefaultObjectKeyStrategy>();
    }

    [Test]
    public async Task MinioObjectStorageProvider_ShouldMapUploadRequestAndCreateBucketWhenConfigured()
    {
        var minioClient = new Mock<IMinioClient>(MockBehavior.Strict);
        BucketExistsArgs? capturedBucketExistsArgs = null;
        MakeBucketArgs? capturedMakeBucketArgs = null;
        PutObjectArgs? capturedPutObjectArgs = null;

        minioClient.Setup(x => x.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
            .Callback<BucketExistsArgs, CancellationToken>((args, _) => capturedBucketExistsArgs = args)
            .ReturnsAsync(false);
        minioClient.Setup(x => x.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()))
            .Callback<MakeBucketArgs, CancellationToken>((args, _) => capturedMakeBucketArgs = args)
            .Returns(Task.CompletedTask);
        minioClient.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectArgs, CancellationToken>((args, _) => capturedPutObjectArgs = args)
            .ReturnsAsync(CreatePutObjectResponse());

        var provider = new MinioObjectStorageProvider(
            minioClient.Object,
            new MinioObjectStorageOptions
            {
                Endpoint = "localhost:9000",
                BucketName = "osa-dev",
                AutoCreateBucket = true
            });

        await using var content = new MemoryStream([1, 2, 3]);
        var result = await provider.PutObjectAsync(
            new ObjectStoragePutRequest
            {
                BucketName = "osa-dev",
                ObjectKey = "up-notification/2026/03/a.pdf",
                Content = content,
                ContentLength = content.Length,
                ContentType = "application/pdf",
                Metadata = ObjectStorageSystemMetadata.Create(
                    Guid.Parse("6dbe48b2-dcef-4159-a5d5-f294d705335d"),
                    "up-notification",
                    "sha256-value")
            });

        result.BucketName.Should().Be("osa-dev");
        result.ObjectKey.Should().Be("up-notification/2026/03/a.pdf");
        capturedBucketExistsArgs.Should().NotBeNull();
        capturedMakeBucketArgs.Should().NotBeNull();
        capturedPutObjectArgs.Should().NotBeNull();
        ReadStringMember(capturedPutObjectArgs!, "Bucket", "BucketName").Should().Be("osa-dev");
        ReadStringMember(capturedPutObjectArgs!, "ObjectName", "Object", "Key").Should().Be("up-notification/2026/03/a.pdf");
        ReadLongMember(capturedPutObjectArgs!, "ObjectSize", "Size").Should().Be(3);
        ReadStringMember(capturedPutObjectArgs!, "ContentType").Should().Be("application/pdf");

        var headers = ReadDictionaryMember(capturedPutObjectArgs!, "Headers", "HeaderParameters");
        headers.Should().ContainKey("x-amz-meta-osa-asset-id");
        headers.Should().ContainKey("x-amz-meta-osa-storage-namespace");
        headers.Should().ContainKey("x-amz-meta-osa-sha256");
    }

    [Test]
    public async Task MinioObjectStorageProvider_ShouldMapDeleteVersionId()
    {
        var minioClient = new Mock<IMinioClient>(MockBehavior.Strict);
        RemoveObjectArgs? capturedRemoveObjectArgs = null;

        minioClient.Setup(x => x.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
            .Callback<RemoveObjectArgs, CancellationToken>((args, _) => capturedRemoveObjectArgs = args)
            .Returns(Task.CompletedTask);

        var provider = new MinioObjectStorageProvider(
            minioClient.Object,
            new MinioObjectStorageOptions
            {
                Endpoint = "localhost:9000",
                BucketName = "osa-dev"
            });

        await provider.DeleteObjectAsync(new ObjectStorageDeleteRequest
        {
            BucketName = "osa-dev",
            ObjectKey = "up-notification/2026/03/a.pdf",
            VersionId = "v1"
        });

        capturedRemoveObjectArgs.Should().NotBeNull();
        ReadStringMember(capturedRemoveObjectArgs!, "Bucket", "BucketName").Should().Be("osa-dev");
        ReadStringMember(capturedRemoveObjectArgs!, "ObjectName", "Object", "Key").Should().Be("up-notification/2026/03/a.pdf");
        ReadStringMember(capturedRemoveObjectArgs!, "VersionId").Should().Be("v1");
    }

    private static PutObjectResponse CreatePutObjectResponse()
    {
        return (PutObjectResponse)RuntimeHelpers.GetUninitializedObject(typeof(PutObjectResponse));
    }

    private static string ReadStringMember(object target, params string[] names)
    {
        return ReadMember<string>(target, names);
    }

    private static long ReadLongMember(object target, params string[] names)
    {
        return ReadMember<long>(target, names);
    }

    private static IReadOnlyDictionary<string, string> ReadDictionaryMember(object target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = TryReadMember(target, name);
            if (value is IReadOnlyDictionary<string, string> readOnlyDictionary)
            {
                return readOnlyDictionary;
            }

            if (value is IDictionary<string, string> dictionary)
            {
                return new Dictionary<string, string>(dictionary, StringComparer.OrdinalIgnoreCase);
            }
        }

        throw new InvalidOperationException(
            $"None of the members '{string.Join(", ", names)}' were found on '{target.GetType().FullName}'.");
    }

    private static T ReadMember<T>(object target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = TryReadMember(target, name);
            if (value is T typed)
            {
                return typed;
            }
        }

        throw new InvalidOperationException(
            $"None of the members '{string.Join(", ", names)}' were found on '{target.GetType().FullName}'.");
    }

    private static object? TryReadMember(object target, string name)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = target.GetType();

        var property = type.GetProperty(name, Flags);
        if (property is not null)
        {
            return property.GetValue(target);
        }

        var field = type.GetField(name, Flags)
            ?? type.GetField($"<{name}>k__BackingField", Flags)
            ?? type.GetFields(Flags).FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? type.GetFields(Flags).FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

        return field?.GetValue(target);
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
        }
    }
}
