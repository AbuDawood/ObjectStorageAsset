namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Resolves the default physical bucket used by the active storage provider.
/// </summary>
public interface IObjectStorageBucketResolver
{
    string GetDefaultBucketName();
}
