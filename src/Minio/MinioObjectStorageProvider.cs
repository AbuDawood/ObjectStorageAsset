using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Minio;

/// <summary>
/// MinIO-backed implementation of the generic object storage provider.
/// </summary>
public sealed class MinioObjectStorageProvider : IObjectStorageProvider, IObjectStorageCopyProvider
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

        var buffer = new FileStream(Path.Combine(Path.GetTempPath(), $"osa-minio-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
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
                VersionId = stat.VersionId ?? request.VersionId
            };
        }
        catch (Exception ex) when (IsMissingObjectException(ex))
        {
            await buffer.DisposeAsync();
            throw Missing(request, ex);
        }
        catch { await buffer.DisposeAsync(); throw; }
    }

    public async Task<ObjectStorageStatResult> CopyToAsync(ObjectStorageGetRequest request, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(destination);
        var args = new GetObjectArgs().WithBucket(request.BucketName).WithObject(request.ObjectKey);
        if (!string.IsNullOrWhiteSpace(request.VersionId)) args = args.WithVersionId(request.VersionId);
        ObjectStat stat;
        try { stat = await _client.GetObjectAsync(args.WithCallbackStream((stream, ct) => stream.CopyToAsync(destination, ct)), cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (IsMissingObjectException(ex)) { throw Missing(request, ex); }
        return new ObjectStorageStatResult
        {
            BucketName = request.BucketName,
            ObjectKey = request.ObjectKey,
            SizeBytes = stat.Size,
            ContentType = stat.ContentType,
            ETag = stat.ETag,
            VersionId = stat.VersionId ?? request.VersionId,
            LastModifiedAtUtc = stat.LastModified
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
                VersionId = stat.VersionId ?? request.VersionId,
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
        return exception is ObjectNotFoundException
            || exception is MinioException { Response.Code: "NoSuchKey" or "NoSuchObject" or "NoSuchVersion" };
    }

    private static ObjectStorageObjectNotFoundException Missing(ObjectStorageGetRequest request, Exception exception)
        => new(request.BucketName, request.ObjectKey, request.VersionId,
            request.VersionId is not null || exception is MinioException { Response.Code: "NoSuchVersion" }
                ? ObjectAssetMissingReason.VersionNotFound : ObjectAssetMissingReason.ObjectNotFound, exception);
}
