using System.Security.Cryptography;
using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.TestSupport.TestDoubles;

/// <summary>
/// In-memory object-storage double that mimics the MinIO identity model: bucket + object key.
/// </summary>
public sealed class InMemoryMinioLikeObjectStorageProvider : IObjectStorageProvider
{
    private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);

    public bool ThrowOnPut { get; set; }

    public bool ThrowOnDelete { get; set; }

    public IReadOnlyCollection<string> ObjectKeys => _objects.Values.Select(x => x.ObjectKey).ToArray();

    public bool ContainsObject(string objectKey)
    {
        return _objects.Values.Any(x => string.Equals(x.ObjectKey, objectKey, StringComparison.Ordinal));
    }

    public void RemoveObject(string objectKey)
    {
        var key = _objects.Keys.FirstOrDefault(
            x => string.Equals(_objects[x].ObjectKey, objectKey, StringComparison.Ordinal));
        if (key is not null)
        {
            _objects.Remove(key);
        }
    }

    public async Task<ObjectStoragePutResult> PutObjectAsync(
        ObjectStoragePutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ThrowOnPut)
        {
            throw new InvalidOperationException("put failed");
        }

        await using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        var bytes = buffer.ToArray();
        var compositeKey = BuildCompositeKey(request.BucketName, request.ObjectKey);
        _objects[compositeKey] = new StoredObject(
            request.BucketName,
            request.ObjectKey,
            bytes,
            request.ContentType,
            DateTimeOffset.UtcNow,
            ComputeEtag(bytes));

        return new ObjectStoragePutResult
        {
            BucketName = request.BucketName,
            ObjectKey = request.ObjectKey,
            ETag = _objects[compositeKey].ETag,
            VersionId = null
        };
    }

    public Task<ObjectStorageGetResult> GetObjectAsync(
        ObjectStorageGetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_objects.TryGetValue(BuildCompositeKey(request.BucketName, request.ObjectKey), out var storedObject))
        {
            throw new InvalidOperationException("object not found");
        }

        return Task.FromResult(new ObjectStorageGetResult
        {
            BucketName = storedObject.BucketName,
            ObjectKey = storedObject.ObjectKey,
            Content = new MemoryStream(storedObject.Bytes, writable: false),
            ContentType = storedObject.ContentType,
            ETag = storedObject.ETag,
            VersionId = request.VersionId
        });
    }

    public Task<ObjectStorageStatResult?> StatObjectAsync(
        ObjectStorageStatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_objects.TryGetValue(BuildCompositeKey(request.BucketName, request.ObjectKey), out var storedObject))
        {
            return Task.FromResult<ObjectStorageStatResult?>(null);
        }

        return Task.FromResult<ObjectStorageStatResult?>(new ObjectStorageStatResult
        {
            BucketName = storedObject.BucketName,
            ObjectKey = storedObject.ObjectKey,
            SizeBytes = storedObject.Bytes.LongLength,
            ContentType = storedObject.ContentType,
            ETag = storedObject.ETag,
            VersionId = request.VersionId,
            LastModifiedAtUtc = storedObject.LastModifiedAtUtc
        });
    }

    public Task DeleteObjectAsync(
        ObjectStorageDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ThrowOnDelete)
        {
            throw new InvalidOperationException("delete failed");
        }

        _objects.Remove(BuildCompositeKey(request.BucketName, request.ObjectKey));
        return Task.CompletedTask;
    }

    private static string BuildCompositeKey(string bucketName, string objectKey)
    {
        return $"{bucketName}\u001f{objectKey}";
    }

    private static string ComputeEtag(byte[] bytes)
    {
        return Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record StoredObject(
        string BucketName,
        string ObjectKey,
        byte[] Bytes,
        string ContentType,
        DateTimeOffset LastModifiedAtUtc,
        string ETag);
}
