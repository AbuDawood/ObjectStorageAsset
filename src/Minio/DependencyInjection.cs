using Microsoft.Extensions.DependencyInjection;
using Elf.ObjectStorageAsset.Application;
using Minio;

namespace Elf.ObjectStorageAsset.Minio;

/// <summary>
/// Host-facing dependency injection entry points for the MinIO provider.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the MinIO provider.
    /// </summary>
    public static IServiceCollection AddObjectStorageAssetMinio(
        this IServiceCollection services,
        Action<MinioObjectStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MinioObjectStorageOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException("ObjectStorageAsset MinIO provider requires an endpoint.");
        }

        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            throw new InvalidOperationException("ObjectStorageAsset MinIO provider requires a bucket name.");
        }

        if (string.IsNullOrWhiteSpace(options.AccessKey))
        {
            throw new InvalidOperationException("ObjectStorageAsset MinIO provider requires an access key.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new InvalidOperationException("ObjectStorageAsset MinIO provider requires a secret key.");
        }

        services.AddSingleton(options);
        services.AddSingleton<IObjectStorageBucketResolver, MinioBucketResolver>();
        services.AddSingleton<IMinioClient>(_ =>
        {
            var client = new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey);

            if (options.UseSsl)
            {
                client = client.WithSSL();
            }

            return client.Build();
        });
        services.AddSingleton<IObjectStorageProvider, MinioObjectStorageProvider>();
        return services;
    }
}
