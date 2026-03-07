using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetContentReader(
    ObjectStorageAssetDbContext objectStorageAssetDbContext,
    IObjectStorageProvider objectStorageProvider)
    : IObjectAssetContentReader
{
    private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;
    private readonly IObjectStorageProvider _objectStorageProvider = objectStorageProvider;

    public async Task<ObjectAssetContentResult?> OpenReadAsync(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        if (assetId == Guid.Empty)
        {
            throw new InvalidOperationException("Object asset content reads require a non-empty asset id.");
        }

        var asset = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == assetId && x.Status == ObjectAssetStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

        if (asset is null)
        {
            return null;
        }

        var getResult = await _objectStorageProvider.GetObjectAsync(
                new ObjectStorageGetRequest
                {
                    BucketName = asset.BucketName,
                    ObjectKey = asset.ObjectKey,
                    VersionId = asset.ProviderVersionId
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new ObjectAssetContentResult
        {
            AssetId = asset.Id,
            FileName = asset.OriginalFileName,
            ContentType = string.IsNullOrWhiteSpace(getResult.ContentType)
                ? asset.ContentType
                : getResult.ContentType,
            SizeBytes = asset.SizeBytes,
            Content = getResult.Content
        };
    }
}
