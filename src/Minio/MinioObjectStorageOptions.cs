namespace Elf.ObjectStorageAsset.Minio;

/// <summary>
/// MinIO provider options.
/// </summary>
public sealed class MinioObjectStorageOptions
{
    /// <summary>
    /// MinIO endpoint URI or host name.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Access key used to authenticate against MinIO.
    /// </summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Secret key used to authenticate against MinIO.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Default bucket name used for object storage operations.
    /// </summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Creates the bucket automatically when it does not exist.
    /// </summary>
    public bool AutoCreateBucket { get; set; }

    /// <summary>
    /// Enables HTTPS for provider connections.
    /// </summary>
    public bool UseSsl { get; set; } = true;
}
