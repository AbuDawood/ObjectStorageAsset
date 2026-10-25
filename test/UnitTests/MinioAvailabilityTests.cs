using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Minio;
using FluentAssertions;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Result;
using Minio.Exceptions;
using Moq;

namespace Elf.ObjectStorageAsset.UnitTests;

public sealed class MinioAvailabilityTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingObject_ShouldBeTypedForBothDownloadPathsAndNullForStat(bool versioned)
    {
        Exception missing = new ObjectNotFoundException();
        if (versioned)
        {
            var error = new MinioException("Missing requested version");
            typeof(MinioException).GetProperty(nameof(MinioException.Response))!.SetValue(error, new ErrorResponse { Code = "NoSuchVersion" });
            missing = error;
        }
        var client = new Mock<IMinioClient>(MockBehavior.Strict);
        client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>())).ThrowsAsync(missing);
        client.Setup(x => x.StatObjectAsync(It.IsAny<StatObjectArgs>(), It.IsAny<CancellationToken>())).ThrowsAsync(missing);
        var provider = new MinioObjectStorageProvider(client.Object, new() { Endpoint = "localhost:9000" });
        var request = new ObjectStorageGetRequest { BucketName = "bucket", ObjectKey = "key", VersionId = versioned ? "v1" : null };
        Func<Task> get = async () => { await provider.GetObjectAsync(request); };
        var result = (await get.Should().ThrowAsync<ObjectStorageObjectNotFoundException>()).Which;
        result.Reason.Should().Be(versioned ? ObjectAssetMissingReason.VersionNotFound : ObjectAssetMissingReason.ObjectNotFound);
        result.InnerException.Should().BeSameAs(missing);
        using var destination = new MemoryStream();
        Func<Task> copy = async () => { await provider.CopyToAsync(request, destination); };
        await copy.Should().ThrowAsync<ObjectStorageObjectNotFoundException>();
        (await provider.StatObjectAsync(new() { BucketName = "bucket", ObjectKey = "key", VersionId = request.VersionId })).Should().BeNull();
    }

    [TestCase("network")]
    [TestCase("authorization")]
    [TestCase("bucket")]
    [TestCase("misleading-message")]
    public async Task OtherErrors_ShouldNeverBecomeMissing(string kind)
    {
        Exception original = kind switch
        {
            "network" => new IOException("Connection lost: NotFound proxy"),
            "authorization" => new UnauthorizedAccessException("AccessDenied"),
            "bucket" => new MinioException("NoSuchBucket"),
            _ => new MinioException("NoSuchKey NotFound in an unstructured error message")
        };
        if (kind == "bucket") typeof(MinioException).GetProperty(nameof(MinioException.Response))!
            .SetValue(original, new ErrorResponse { Code = "NoSuchBucket" });
        var client = new Mock<IMinioClient>(MockBehavior.Strict);
        client.Setup(x => x.StatObjectAsync(It.IsAny<StatObjectArgs>(), It.IsAny<CancellationToken>())).ThrowsAsync(original);
        client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>())).ThrowsAsync(original);
        var provider = new MinioObjectStorageProvider(client.Object, new() { Endpoint = "localhost:9000" });
        Func<Task> stat = async () => { await provider.StatObjectAsync(new() { BucketName = "b", ObjectKey = "k" }); };
        (await stat.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(original);
        Func<Task> get = async () => { await provider.GetObjectAsync(new() { BucketName = "b", ObjectKey = "k" }); };
        (await get.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(original);
        using var destination = new MemoryStream();
        Func<Task> copy = async () => { await provider.CopyToAsync(new() { BucketName = "b", ObjectKey = "k" }, destination); };
        (await copy.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(original);
    }
}
