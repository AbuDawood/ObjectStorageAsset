namespace Elf.ObjectStorageAsset.Domain.Enums;

/// <summary>
/// Lifecycle state for one stored object asset.
/// </summary>
public enum ObjectAssetStatus
{
    PendingUpload = 1,
    Active = 2,
    UploadFailed = 3,
    PendingDelete = 4,
    Deleted = 5,
    DeleteFailed = 6
}
