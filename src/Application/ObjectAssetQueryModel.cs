using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Read-only query model mapped by host DbContexts to the ObjectAssets table.
/// </summary>
public sealed class ObjectAssetQueryModel
{
    public Guid Id { get; set; }

    public string OwnerType { get; set; } = string.Empty;

    public ObjectAssetOwnerKeyKind OwnerKeyKind { get; set; }

    public long? OwnerKeyInt64 { get; set; }

    public Guid? OwnerKeyGuid { get; set; }

    public string? OwnerKeyText { get; set; }

    public string SlotName { get; set; } = string.Empty;

    public ObjectAssetSlotMultiplicity SlotMultiplicity { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public ObjectAssetStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
