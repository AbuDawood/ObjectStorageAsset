namespace Elf.ObjectStorageAsset.Domain.Enums;

/// <summary>
/// Supported persisted owner key shapes.
/// </summary>
public enum ObjectAssetOwnerKeyKind
{
    Unassigned = 0,
    Int64 = 1,
    Guid = 2,
    String = 3
}
