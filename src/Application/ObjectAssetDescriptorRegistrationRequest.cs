using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Registers one descriptor into the local OSA catalog.
/// </summary>
public sealed class ObjectAssetDescriptorRegistrationRequest
{
    public required ObjectAssetDescriptor Descriptor { get; init; }

    public ObjectAssetOwnershipMode OwnershipMode { get; init; } = ObjectAssetOwnershipMode.Referenced;
}
