using Minio;
using Minio.DataModel.Args;
using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.Minio;

/// <summary>
/// MinIO-backed implementation of the generic object storage provider.
/// </summary>
public sealed class MinioObjectStorageProvider : IObjectStorageProvider
{
    private readonly IMinioClient _client;
    private readonly MinioObjectStorageOptions _options;

    public MinioObjectStorageProvider(IMinioClient client, MinioObjectStorageOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ObjectStoragePutResult> PutObjectAsync(
        ObjectStoragePutRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidatePutRequest(request);
        await EnsureBucketExistsAsync(request.BucketName, cancellationToken).ConfigureAwait(false);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(request.BucketName)
            .WithObject(request.ObjectKey)
            .WithStreamData(request.Content)
            .WithObjectSize(request.ContentLength)
            .WithContentType(request.ContentType);

        if (request.Metadata.Count > 0)
        {
            putObjectArgs = putObjectArgs.WithHeaders(BuildMetadataHeaders(request.Metadata));
        }

        var response = await _client.PutObjectAsync(putObjectArgs, cancellationToken).ConfigureAwait(false);

        return new ObjectStoragePutResult
        {
            BucketName = request.BucketName,
            ObjectKey = request.ObjectKey,
            ETag = response.Etag,
            VersionId = null
        };
    }

    public async Task<ObjectStorageGetResult> GetObjectAsync(
        ObjectStorageGetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var getObjectArgs = new GetObjectArgs()
            .WithBucket(request.BucketName)
            .WithObject(request.ObjectKey);

        if (!string.IsNullOrWhiteSpace(request.VersionId))
        {
            getObjectArgs = getObjectArgs.WithVersionId(request.VersionId);
        }

        var buffer = new MemoryStream();
        var stat = await _client.GetObjectAsync(
                getObjectArgs.WithCallbackStream(stream => stream.CopyTo(buffer)),
                cancellationToken)
            .ConfigureAwait(false);
        buffer.Position = 0;

        return new ObjectStorageGetResult
        {
            BucketName = request.BucketName,
            ObjectKey = request.ObjectKey,
            Content = buffer,
            ContentType = string.IsNullOrWhiteSpace(stat.ContentType)
                ? "application/octet-stream"
                : stat.ContentType,
            ETag = stat.ETag,
            VersionId = request.VersionId
        };
    }

    public async Task<ObjectStorageStatResult?> StatObjectAsync(
        ObjectStorageStatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var statObjectArgs = new StatObjectArgs()
            .WithBucket(request.BucketName)
            .WithObject(request.ObjectKey);

        if (!string.IsNullOrWhiteSpace(request.VersionId))
        {
            statObjectArgs = statObjectArgs.WithVersionId(request.VersionId);
        }

        try
        {
            var stat = await _client.StatObjectAsync(statObjectArgs, cancellationToken).ConfigureAwait(false);
            return new ObjectStorageStatResult
            {
                BucketName = request.BucketName,
                ObjectKey = request.ObjectKey,
                SizeBytes = stat.Size,
                ContentType = stat.ContentType,
                ETag = stat.ETag,
                VersionId = request.VersionId,
                LastModifiedAtUtc = stat.LastModified
            };
        }
        catch (Exception ex) when (IsMissingObjectException(ex))
        {
            return null;
        }
    }

    public async Task DeleteObjectAsync(
        ObjectStorageDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateDeleteRequest(request);

        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(request.BucketName)
            .WithObject(request.ObjectKey);

        if (!string.IsNullOrWhiteSpace(request.VersionId))
        {
            removeObjectArgs = removeObjectArgs.WithVersionId(request.VersionId);
        }

        await _client.RemoveObjectAsync(removeObjectArgs, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken)
    {
        if (!_options.AutoCreateBucket)
        {
            return;
        }

        var bucketExists = await _client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucketName),
                cancellationToken)
            .ConfigureAwait(false);

        if (bucketExists)
        {
            return;
        }

        await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(bucketName),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidatePutRequest(ObjectStoragePutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BucketName))
        {
            throw new InvalidOperationException("MinIO upload requires a bucket name.");
        }

        if (string.IsNullOrWhiteSpace(request.ObjectKey))
        {
            throw new InvalidOperationException("MinIO upload requires an object key.");
        }

        if (request.Content is null)
        {
            throw new InvalidOperationException("MinIO upload requires a content stream.");
        }

        if (request.ContentLength < 0)
        {
            throw new InvalidOperationException("MinIO upload requires a non-negative content length.");
        }
    }

    private static void ValidateDeleteRequest(ObjectStorageDeleteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BucketName))
        {
            throw new InvalidOperationException("MinIO delete requires a bucket name.");
        }

        if (string.IsNullOrWhiteSpace(request.ObjectKey))
        {
            throw new InvalidOperationException("MinIO delete requires an object key.");
        }
    }

    private static Dictionary<string, string> BuildMetadataHeaders(IReadOnlyDictionary<string, string> metadata)
    {
        return metadata.ToDictionary(
            pair => $"x-amz-meta-{pair.Key}",
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsMissingObjectException(Exception exception)
    {
        return exception.Message.Contains("NoSuchKey", StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains("NoSuchBucket", StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains("NotFound", StringComparison.OrdinalIgnoreCase);
    }
}
