namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Opens physical object content for one stored asset.
/// </summary>
public interface IObjectAssetContentReader
{
    Task<ObjectAssetContentResult?> OpenReadAsync(Guid assetId, CancellationToken cancellationToken = default);
}
