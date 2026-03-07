using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.TestSupport.TestDoubles;

public sealed class FixedBucketResolver(string bucketName) : IObjectStorageBucketResolver
{
    public string GetDefaultBucketName() => bucketName;
}
