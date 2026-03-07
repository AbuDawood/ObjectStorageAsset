namespace Elf.ObjectStorageAsset.Domain.Enums;

/// <summary>
/// Controls whether OSA owns the physical object lifecycle or only references it.
/// </summary>
public enum ObjectAssetOwnershipMode
{
    Managed = 1,
    Referenced = 2
}
