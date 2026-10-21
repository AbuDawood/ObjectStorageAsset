using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetContentReader(
    ObjectStorageAssetDbContext objectStorageAssetDbContext,
    IObjectStorageProvider objectStorageProvider,
    ObjectAssetIntegrityService integrity,
    ObjectStorageAssetRuntimeOptions options)
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

        var opened = await integrity.OpenAssetAsync(assetId, options.Integrity.Triggers.HasFlag(Sha256VerificationTriggers.OnRead),
            ObjectAssetReadScope.Active, cancellationToken);
        if (opened is null)
        {
            return null;
        }
        var asset = opened.Asset;

        return new ObjectAssetContentResult
        {
            AssetId = asset.Id,
            FileName = asset.OriginalFileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            Content = opened.Content,
            Integrity = opened.Integrity
        };
    }
}
