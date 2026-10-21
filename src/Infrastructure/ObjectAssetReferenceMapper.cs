using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using System.Linq.Expressions;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal static class ObjectAssetReferenceMapper
{
    internal static readonly Expression<Func<ObjectAsset, ObjectAssetReferenceDto>> Projection = asset => new()
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
