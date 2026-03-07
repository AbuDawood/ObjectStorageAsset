using System.Text;
using FluentAssertions;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.TestSupport.TestDoubles;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class InMemoryMinioLikeProviderTests
{
    [Test]
    public async Task PutObjectAsync_ShouldOverwriteWithinSameBucketAndKey()
    {
        var provider = new InMemoryMinioLikeObjectStorageProvider();

        await provider.PutObjectAsync(new ObjectStoragePutRequest
        {
            BucketName = "bucket-a",
            ObjectKey = "path/file.txt",
            Content = CreateStream("v1"),
            ContentLength = 2,
            ContentType = "text/plain"
        });
        await provider.PutObjectAsync(new ObjectStoragePutRequest
        {
            BucketName = "bucket-a",
            ObjectKey = "path/file.txt",
            Content = CreateStream("v2"),
            ContentLength = 2,
            ContentType = "text/plain"
        });

        var result = await provider.GetObjectAsync(new ObjectStorageGetRequest
        {
            BucketName = "bucket-a",
            ObjectKey = "path/file.txt"
        });

        await using var content = result.Content;
        using var reader = new StreamReader(content, Encoding.UTF8);
        (await reader.ReadToEndAsync()).Should().Be("v2");
        provider.ObjectKeys.Should().ContainSingle();
    }

    [Test]
    public async Task PutObjectAsync_ShouldKeepSameObjectKeyAcrossDifferentBuckets()
    {
        var provider = new InMemoryMinioLikeObjectStorageProvider();

        await provider.PutObjectAsync(new ObjectStoragePutRequest
        {
            BucketName = "bucket-a",
            ObjectKey = "shared/file.txt",
            Content = CreateStream("a"),
            ContentLength = 1,
            ContentType = "text/plain"
        });
        await provider.PutObjectAsync(new ObjectStoragePutRequest
        {
            BucketName = "bucket-b",
            ObjectKey = "shared/file.txt",
            Content = CreateStream("b"),
            ContentLength = 1,
            ContentType = "text/plain"
        });

        var bucketA = await provider.GetObjectAsync(new ObjectStorageGetRequest
        {
            BucketName = "bucket-a",
            ObjectKey = "shared/file.txt"
        });
        var bucketB = await provider.GetObjectAsync(new ObjectStorageGetRequest
        {
            BucketName = "bucket-b",
            ObjectKey = "shared/file.txt"
        });

        await using var bucketAContent = bucketA.Content;
        using var bucketAReader = new StreamReader(bucketAContent, Encoding.UTF8);
        (await bucketAReader.ReadToEndAsync()).Should().Be("a");

        await using var bucketBContent = bucketB.Content;
        using var bucketBReader = new StreamReader(bucketBContent, Encoding.UTF8);
        (await bucketBReader.ReadToEndAsync()).Should().Be("b");
    }

    private static MemoryStream CreateStream(string content)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }
}
