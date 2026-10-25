using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Domain.Entities;

/// <summary>
/// Aggregate root for one object-storage-backed file.
/// </summary>
public abstract class ObjectAsset
{
    protected ObjectAsset()
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

    public ObjectAssetBindingKind BindingKind { get; private set; }
    public ObjectAssetOriginKind OriginKind { get; private set; }
    public Guid? OriginInstanceId { get; private set; }
    public Guid? OriginAssetId { get; private set; }
    public Guid? ExternalOwnerInstanceId { get; private set; }
    public string? ExternalOwnerType { get; private set; }
    public ObjectAssetOwnerKeyKind? ExternalOwnerKeyKind { get; private set; }
    public string? ExternalOwnerKey { get; private set; }
    public Guid StorageAuthorityId { get; private set; }
    public string? NamingProfileId { get; private set; }
    public int? NamingProfileVersion { get; private set; }
    public string? NamingDefinitionHash { get; private set; }
    public Sha256ValidationStatus Sha256ValidationStatus { get; private set; }
    public DateTimeOffset? Sha256ValidationAttemptedAtUtc { get; private set; }
    public DateTimeOffset? Sha256ValidationCompletedAtUtc { get; private set; }
    public string? Sha256ValidationActualHash { get; private set; }
    public string? Sha256ValidationObjectVersion { get; private set; }
    public string? Sha256ValidationError { get; private set; }
    public int DeleteAttemptCount { get; private set; }
    public DateTimeOffset? NextDeleteAttemptAtUtc { get; private set; }
    public bool DeleteRetrySuspended { get; private set; }
    public bool? PhysicalDeletionRequested { get; private set; }

    public void SetCreationProvenance(Guid instanceId, Guid authorityId)
    {
        if (OriginInstanceId.HasValue && (OriginInstanceId != instanceId || OriginAssetId != Id))
            throw new InvalidOperationException("Known asset origin cannot be replaced.");
        OriginKind = ObjectAssetOriginKind.Local;
        OriginInstanceId = instanceId == Guid.Empty ? null : instanceId;
        OriginAssetId = Id;
        StorageAuthorityId = authorityId;
    }

    public void SetLegacyStorageAuthority(Guid authorityId) => StorageAuthorityId = authorityId;
    public void SetImportedOrigin(Guid instanceId, Guid assetId)
    {
        if (instanceId == Guid.Empty || assetId == Guid.Empty) throw new InvalidOperationException("Complete origin is required.");
        if (OriginInstanceId.HasValue && (OriginInstanceId != instanceId || OriginAssetId != assetId))
            throw new InvalidOperationException("Known asset origin cannot be replaced.");
        OriginKind = ObjectAssetOriginKind.ImportedFromOsa; OriginInstanceId = instanceId; OriginAssetId = assetId;
    }

    public void SetNamingProvenance(string profileId, int version, string hash)
    {
        if (string.IsNullOrWhiteSpace(profileId) || profileId.Length > 128 || version < 1
            || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Naming provenance requires a profile id, positive version and SHA-256 definition hash.");
        if (NamingProfileId is not null && (NamingProfileId != profileId || NamingProfileVersion != version
            || !string.Equals(NamingDefinitionHash, hash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Known naming provenance cannot be replaced.");
        NamingProfileId = profileId;
        NamingProfileVersion = version;
        NamingDefinitionHash = hash;
    }

    public void SetExternalOwnership(Guid instanceId, string ownerType, ObjectAssetOwnerKeyKind keyKind,
        string ownerKey, Guid originInstanceId, Guid originAssetId, Guid storageAuthorityId)
    {
        if (instanceId == Guid.Empty
            || string.IsNullOrWhiteSpace(ownerType) || string.IsNullOrWhiteSpace(ownerKey)
            || keyKind == ObjectAssetOwnerKeyKind.Unassigned)
            throw new InvalidOperationException("External ownership requires a complete owner reference.");
        if ((originInstanceId == Guid.Empty) != (originAssetId == Guid.Empty))
            throw new InvalidOperationException("Origin references must be complete or explicitly unknown.");
        if (originInstanceId != Guid.Empty && OriginInstanceId.HasValue && (OriginInstanceId != originInstanceId || OriginAssetId != originAssetId))
            throw new InvalidOperationException("Known asset origin cannot be replaced.");
        ClearOwnerBinding();
        BindingKind = ObjectAssetBindingKind.ExternalOwner;
        ExternalOwnerInstanceId = instanceId;
        ExternalOwnerType = ownerType.Trim();
        ExternalOwnerKeyKind = keyKind;
        ExternalOwnerKey = ownerKey.Trim();
        if (originInstanceId != Guid.Empty) SetImportedOrigin(originInstanceId, originAssetId);
        StorageAuthorityId = storageAuthorityId;
    }

    public void RecordSha256Validation(Sha256ValidationStatus status, DateTimeOffset attemptedAtUtc,
        DateTimeOffset? completedAtUtc, string? actualHash, string? objectVersion, string? error)
    {
        if (Sha256ValidationAttemptedAtUtc > attemptedAtUtc) return;
        Sha256ValidationStatus = status;
        Sha256ValidationAttemptedAtUtc = attemptedAtUtc;
        Sha256ValidationCompletedAtUtc = completedAtUtc;
        Sha256ValidationActualHash = actualHash;
        Sha256ValidationObjectVersion = objectVersion;
        Sha256ValidationError = string.IsNullOrEmpty(error) ? null : error[..Math.Min(error.Length, 1024)];
    }

    public void ScheduleDeleteRetry(DateTimeOffset nextAttempt, int maxAttempts)
    {
        DeleteAttemptCount++;
        NextDeleteAttemptAtUtc = nextAttempt;
        DeleteRetrySuspended = DeleteAttemptCount >= maxAttempts;
    }

    public ObjectAsset CopyToLocation(ObjectAssetLocation location)
    {
        ObjectAsset target = location switch
        {
            ObjectAssetLocation.Main => new PermanentObjectAsset(),
            ObjectAssetLocation.Temporary => new TemporaryObjectAsset(),
            ObjectAssetLocation.Archive => new ArchivedObjectAsset(),
            _ => throw new ArgumentOutOfRangeException(nameof(location))
        };
        foreach (var property in typeof(ObjectAsset).GetProperties())
            if (property.SetMethod is not null) property.SetValue(target, property.GetValue(this));
        target.RowVersion = [];
        return target;
    }

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

        return new PermanentObjectAsset
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

        return new PermanentObjectAsset
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
        EnsureCanBind();
        BindingKind = ObjectAssetBindingKind.LocalOwner;
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

        EnsureCanBind();
        BindingKind = ObjectAssetBindingKind.LocalOwner;

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

        EnsureCanBind();
        BindingKind = ObjectAssetBindingKind.LocalOwner;

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
        BindingKind = ObjectAssetBindingKind.Temporary;
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
        BindingKind = ObjectAssetBindingKind.LegacyUnresolved;
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

    public void RecordStorageVersion(string? etag, string? versionId)
    {
        ETag = etag;
        ProviderVersionId = versionId;
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

    public void MarkPendingDelete(DateTimeOffset deletedAtUtc, bool physicallyDelete)
    {
        PhysicalDeletionRequested = physicallyDelete;
        MarkPendingDelete(deletedAtUtc);
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
        return BindingKind == ObjectAssetBindingKind.LegacyUnresolved;
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
            : errorMessage.Trim()[..Math.Min(errorMessage.Trim().Length, 1024)];
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

    private void EnsureCanBind()
    {
        if (Status is ObjectAssetStatus.Deleted or ObjectAssetStatus.PendingDelete or ObjectAssetStatus.DeleteFailed
            || HasExpiredTemporaryBinding(DateTimeOffset.UtcNow))
            throw new InvalidOperationException("Deleted, deleting, or expired assets cannot be bound.");
        ExternalOwnerInstanceId = null;
        ExternalOwnerType = null;
        ExternalOwnerKeyKind = null;
        ExternalOwnerKey = null;
    }
}
