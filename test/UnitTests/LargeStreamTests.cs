using System.Security.Cryptography;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure;
using Elf.ObjectStorageAsset.TestSupport.TestDoubles;
using FluentAssertions;
using Moq;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class LargeStreamTests
{
    [Test]
    public async Task LargeInput_ShouldSpillHashCorrectlyAndRemoveTemporaryFile()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "staging", Guid.NewGuid().ToString("N"));
        var options = new ObjectStorageAssetRuntimeOptions
        {
            DefaultStorageNamespace = "large",
            Buffering = new() { TemporaryDirectory = directory, MemoryThresholdBytes = 65536, MaximumMemoryBytes = 131072 }
        };
        var stager = new ObjectAssetStager(options, new DefaultObjectKeyStrategy(options));
        const long size = 64L * 1024 * 1024;
        try
        {
            await using (var source = new GeneratedReadStream(size))
            await using (var staged = await stager.StageAsync(source, "large.bin", null, null, null, default))
            {
                staged.Content.Length.Should().Be(size); staged.Content.CanWrite.Should().BeFalse();
                Directory.GetFiles(directory).Should().HaveCount(1);
                await using var expected = new GeneratedReadStream(size);
                staged.Sha256.Should().Be(Convert.ToHexString(await SHA256.HashDataAsync(expected)).ToLowerInvariant());
            }
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Test]
    public async Task ProviderCopy_ShouldReturnObservedVersionAndReleaseQuotaAfterFailure()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "staging", Guid.NewGuid().ToString("N"));
        var options = new ObjectStorageAssetRuntimeOptions
        {
            Buffering = new()
            {
                TemporaryDirectory = directory,
                MaximumFileBytes = 1024 * 1024,
                MaximumStagedBytes = 1024 * 1024,
                MaximumStagedFiles = 1
            }
        };
        var stager = new ObjectAssetStager(options, new DefaultObjectKeyStrategy());
        var provider = new Mock<IObjectStorageProvider>(MockBehavior.Strict);
        long size = 2 * 1024 * 1024;
        provider.As<IObjectStorageCopyProvider>().Setup(x => x.CopyToAsync(It.IsAny<ObjectStorageGetRequest>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(async (ObjectStorageGetRequest request, Stream destination, CancellationToken ct) =>
            {
                await using var source = new GeneratedReadStream(size); await source.CopyToAsync(destination, ct);
                return new ObjectStorageStatResult { BucketName = request.BucketName, ObjectKey = request.ObjectKey, VersionId = "observed-version" };
            });
        var request = new ObjectStorageGetRequest { BucketName = "bucket", ObjectKey = "key" };
        try
        {
            Func<Task> tooLarge = async () => { var result = await stager.DownloadAsync(provider.Object, request, default); await result.Content.DisposeAsync(); };
            await tooLarge.Should().ThrowAsync<IOException>();
            Directory.GetFiles(directory).Should().BeEmpty();
            size = 65536;
            var downloaded = await stager.DownloadAsync(provider.Object, request, default);
            await using (downloaded.Content)
            {
                downloaded.ObjectVersion.Should().Be("observed-version");
                downloaded.Content.Length.Should().Be(size);
            }
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
}
