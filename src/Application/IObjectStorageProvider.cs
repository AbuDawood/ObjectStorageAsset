namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Abstraction over the physical object storage provider.
/// </summary>
public interface IObjectStorageProvider
{
    Task<ObjectStoragePutResult> PutObjectAsync(
        ObjectStoragePutRequest request,
        CancellationToken cancellationToken = default);

    Task<ObjectStorageGetResult> GetObjectAsync(
        ObjectStorageGetRequest request,
        CancellationToken cancellationToken = default);

    Task<ObjectStorageStatResult?> StatObjectAsync(
        ObjectStorageStatRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteObjectAsync(
        ObjectStorageDeleteRequest request,
        CancellationToken cancellationToken = default);
}
