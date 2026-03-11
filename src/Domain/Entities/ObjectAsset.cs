using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Domain.Entities;

/// <summary>
/// Aggregate root for one object-storage-backed file.
/// </summary>
public sealed class ObjectAsset
{
    private ObjectAsset()
    {
    }

    public Guid Id { get; private set; }

    public string OwnerType { get; private set; } = string.Empty;

    public ObjectAssetOwnerKeyKind OwnerKeyKind { get; private set; }

    public long? OwnerKeyInt64 { get; private set; }

    public Guid? OwnerKeyGuid { get; private set; }

    public string? OwnerKeyText { get; private set; }

    public string SlotName { get; private set; } = string.Empty;

    public ObjectAssetSlotMultiplicity SlotMultiplicity { get; private set; }

    public string BucketName { get; private set; } = string.Empty;

    public string StorageNamespace { get; private set; } = string.Empty;

    public string ObjectKey { get; private set; } = string.Empty;

    public string OriginalFileName { get; private set; } = string.Empty;

    public string Extension { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; } = string.Empty;

    public string? ETag { get; private set; }

    public string? ProviderVersionId { get; private set; }

    public ObjectAssetOwnershipMode OwnershipMode { get; private set; }

    public string CustomMetadataJson { get; private set; } = "{}";

    public ObjectAssetStatus Status { get; private set; }

    public Guid? TemporaryBindingId { get; private set; }

    public DateTimeOffset? TemporaryBindingExpiresAtUtc { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public DateTimeOffset? PhysicalDeletedAtUtc { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UploadedAtUtc { get; private set; }

    public DateTimeOffset LastStatusChangedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static ObjectAsset CreatePendingUpload(
        Guid id,
        string ownerType,
        string slotName,
        ObjectAssetSlotMultiplicity slotMultiplicity,
        string bucketName,
        string storageNamespace,
        string objectKey,
        string originalFileName,
        string extension,
        string contentType,
        DateTimeOffset createdAtUtc,
        long sizeBytes = 0,
        string? sha256 = null,
        DateTimeOffset? expiresAtUtc = null,
        ObjectAssetOwnershipMode ownershipMode = ObjectAssetOwnershipMode.Managed,
        string? customMetadataJson = null)
    {
        if (id == Guid.Empty)
        {
            throw new InvalidOperationException("Object asset id is required.");
        }

        if (string.IsNullOrWhiteSpace(ownerType))
        {
            throw new InvalidOperationException("OwnerType is required.");
        }

        if (string.IsNullOrWhiteSpace(slotName))
        {
            throw new InvalidOperationException("SlotName is required.");
        }

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new InvalidOperationException("BucketName is required.");
        }

        if (string.IsNullOrWhiteSpace(storageNamespace))
        {
            throw new InvalidOperationException("StorageNamespace is required.");
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new InvalidOperationException("ObjectKey is required.");
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new InvalidOperationException("OriginalFileName is required.");
        }

        return new ObjectAsset
        {
            Id = id,
            OwnerType = ownerType.Trim(),
            SlotName = slotName.Trim(),
            SlotMultiplicity = slotMultiplicity,
            BucketName = bucketName.Trim(),
            StorageNamespace = storageNamespace.Trim(),
            ObjectKey = objectKey.Trim(),
            OriginalFileName = originalFileName.Trim(),
            Extension = extension.Trim(),
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType.Trim(),
            SizeBytes = sizeBytes,
            Sha256 = string.IsNullOrWhiteSpace(sha256) ? string.Empty : sha256.Trim(),
            OwnershipMode = ownershipMode,
            CustomMetadataJson = NormalizeCustomMetadataJson(customMetadataJson),
            Status = ObjectAssetStatus.PendingUpload,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = createdAtUtc,
            LastStatusChangedAtUtc = createdAtUtc
        };
    }

    public static ObjectAsset CreateRegisteredDescriptor(
        Guid id,
        string bucketName,
        string objectKey,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string? sha256,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc,
        ObjectAssetOwnershipMode ownershipMode,
        string? customMetadataJson = null)
    {
        if (id == Guid.Empty)
        {
            throw new InvalidOperationException("Object asset id is required.");
        }

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new InvalidOperationException("BucketName is required.");
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new InvalidOperationException("ObjectKey is required.");
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new InvalidOperationException("OriginalFileName is required.");
        }

        return new ObjectAsset
        {
            Id = id,
            OwnerType = string.Empty,
            OwnerKeyKind = ObjectAssetOwnerKeyKind.Unassigned,
            SlotName = string.Empty,
            SlotMultiplicity = ObjectAssetSlotMultiplicity.Many,
            BucketName = bucketName.Trim(),
            StorageNamespace = string.Empty,
            ObjectKey = objectKey.Trim(),
            OriginalFileName = originalFileName.Trim(),
            Extension = Path.GetExtension(originalFileName)?.Trim() ?? string.Empty,
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType.Trim(),
            SizeBytes = sizeBytes,
            Sha256 = string.IsNullOrWhiteSpace(sha256) ? string.Empty : sha256.Trim(),
            OwnershipMode = ownershipMode,
            CustomMetadataJson = NormalizeCustomMetadataJson(customMetadataJson),
            Status = ObjectAssetStatus.Active,
            ExpiresAtUtc = null,
            CreatedAtUtc = createdAtUtc,
            UploadedAtUtc = createdAtUtc,
            LastStatusChangedAtUtc = createdAtUtc
        };
    }

    public void BindOwner(long ownerKey)
    {
        OwnerKeyKind = ObjectAssetOwnerKeyKind.Int64;
        OwnerKeyInt64 = ownerKey;
        OwnerKeyGuid = null;
        OwnerKeyText = null;
        ClearTemporaryBinding();
    }

    public void BindOwner(Guid ownerKey)
    {
        if (ownerKey == Guid.Empty)
        {
            throw new InvalidOperationException("Guid owner key is required.");
        }

        OwnerKeyKind = ObjectAssetOwnerKeyKind.Guid;
        OwnerKeyInt64 = null;
        OwnerKeyGuid = ownerKey;
        OwnerKeyText = null;
        ClearTemporaryBinding();
    }

    public void BindOwner(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            throw new InvalidOperationException("String owner key is required.");
        }

        OwnerKeyKind = ObjectAssetOwnerKeyKind.String;
        OwnerKeyInt64 = null;
        OwnerKeyGuid = null;
        OwnerKeyText = ownerKey.Trim();
        ClearTemporaryBinding();
    }

    public void BindTemporary(
        string ownerType,
        string slotName,
        ObjectAssetSlotMultiplicity slotMultiplicity,
        Guid temporaryBindingId,
        DateTimeOffset? temporaryBindingExpiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(ownerType))
        {
            throw new InvalidOperationException("OwnerType is required.");
        }

        if (string.IsNullOrWhiteSpace(slotName))
        {
            throw new InvalidOperationException("SlotName is required.");
        }

        if (temporaryBindingId == Guid.Empty)
        {
            throw new InvalidOperationException("Temporary binding id is required.");
        }

        OwnerType = ownerType.Trim();
        SlotName = slotName.Trim();
        SlotMultiplicity = slotMultiplicity;
        OwnerKeyKind = ObjectAssetOwnerKeyKind.Unassigned;
        OwnerKeyInt64 = null;
        OwnerKeyGuid = null;
        OwnerKeyText = null;
        TemporaryBindingId = temporaryBindingId;
        TemporaryBindingExpiresAtUtc = temporaryBindingExpiresAtUtc;
    }

    public void SetOwnerSlot(
        string ownerType,
        string slotName,
        ObjectAssetSlotMultiplicity slotMultiplicity)
    {
        if (string.IsNullOrWhiteSpace(ownerType))
        {
            throw new InvalidOperationException("OwnerType is required.");
        }

        if (string.IsNullOrWhiteSpace(slotName))
        {
            throw new InvalidOperationException("SlotName is required.");
        }

        OwnerType = ownerType.Trim();
        SlotName = slotName.Trim();
        SlotMultiplicity = slotMultiplicity;
    }

    public void ClearOwnerBinding()
    {
        OwnerKeyKind = ObjectAssetOwnerKeyKind.Unassigned;
        OwnerKeyInt64 = null;
        OwnerKeyGuid = null;
        OwnerKeyText = null;
        ClearTemporaryBinding();
    }

    public void MarkUploadSucceeded(
        DateTimeOffset uploadedAtUtc,
        string? eTag,
        string? providerVersionId)
    {
        UploadedAtUtc = uploadedAtUtc;
        ETag = string.IsNullOrWhiteSpace(eTag) ? null : eTag.Trim();
        ProviderVersionId = string.IsNullOrWhiteSpace(providerVersionId) ? null : providerVersionId.Trim();
        ExpiresAtUtc = null;
        ErrorMessage = null;
        Status = ObjectAssetStatus.Active;
        LastStatusChangedAtUtc = uploadedAtUtc;
    }

    public void MarkUploadFailed(DateTimeOffset failedAtUtc, string? errorMessage)
    {
        Status = ObjectAssetStatus.UploadFailed;
        ErrorMessage = NormalizeErrorMessage(errorMessage);
        LastStatusChangedAtUtc = failedAtUtc;
    }

    public void MarkPendingDelete(DateTimeOffset deletedAtUtc)
    {
        DeletedAtUtc = deletedAtUtc;
        Status = ObjectAssetStatus.PendingDelete;
        ErrorMessage = null;
        LastStatusChangedAtUtc = deletedAtUtc;
    }

    public void MarkDeleted(DateTimeOffset deletedAtUtc, bool physicallyDeleted)
    {
        DeletedAtUtc = deletedAtUtc;
        if (physicallyDeleted)
        {
            PhysicalDeletedAtUtc = deletedAtUtc;
        }

        Status = ObjectAssetStatus.Deleted;
        ErrorMessage = null;
        LastStatusChangedAtUtc = deletedAtUtc;
    }

    public void MarkDeleteFailed(DateTimeOffset failedAtUtc, string? errorMessage)
    {
        DeletedAtUtc ??= failedAtUtc;
        Status = ObjectAssetStatus.DeleteFailed;
        ErrorMessage = NormalizeErrorMessage(errorMessage);
        LastStatusChangedAtUtc = failedAtUtc;
    }

    public bool IsReadable() => Status == ObjectAssetStatus.Active;

    public bool IsOwnerBound()
    {
        return OwnerKeyKind != ObjectAssetOwnerKeyKind.Unassigned;
    }

    public bool IsTemporarilyBound()
    {
        return TemporaryBindingId.HasValue;
    }

    public bool IsUnbound()
    {
        return !IsOwnerBound() && !IsTemporarilyBound();
    }

    public bool HasExpired(DateTimeOffset utcNow)
    {
        return ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= utcNow;
    }

    public bool HasExpiredTemporaryBinding(DateTimeOffset utcNow)
    {
        return TemporaryBindingId.HasValue
               && TemporaryBindingExpiresAtUtc.HasValue
               && TemporaryBindingExpiresAtUtc.Value <= utcNow;
    }

    private static string? NormalizeErrorMessage(string? errorMessage)
    {
        return string.IsNullOrWhiteSpace(errorMessage)
            ? null
            : errorMessage.Trim();
    }

    private static string NormalizeCustomMetadataJson(string? customMetadataJson)
    {
        return string.IsNullOrWhiteSpace(customMetadataJson)
            ? "{}"
            : customMetadataJson.Trim();
    }

    private void ClearTemporaryBinding()
    {
        TemporaryBindingId = null;
        TemporaryBindingExpiresAtUtc = null;
    }
}
