using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal static class ObjectAssetReferenceMapper
{
    public static ObjectAssetReferenceDto Map(ObjectAsset asset)
    {
        return new ObjectAssetReferenceDto
        {
            AssetId = asset.Id,
            SlotName = asset.SlotName,
            FileName = asset.OriginalFileName,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            Status = asset.Status,
            CreatedAtUtc = asset.CreatedAtUtc,
            ExpiresAtUtc = null
        };
    }
}
