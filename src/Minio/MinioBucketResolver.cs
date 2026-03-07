using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.Minio;

internal sealed class MinioBucketResolver(MinioObjectStorageOptions options) : IObjectStorageBucketResolver
{
    public string GetDefaultBucketName()
    {
        return options.BucketName;
    }
}
