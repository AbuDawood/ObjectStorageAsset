using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

public sealed record ObjectAssetCursor(DateTimeOffset Timestamp, Guid AssetId);
public sealed record ObjectAssetPageRequest(int PageSize = 100, ObjectAssetCursor? After = null);
public sealed record ObjectAssetPageItem(Guid AssetId, string FileName, string ContentType, long SizeBytes,
    DateTimeOffset CreatedAtUtc, ObjectAssetStatus Status, ObjectAssetBindingKind BindingKind,
    Sha256ValidationStatus Sha256ValidationStatus, DateTimeOffset? DeletedAtUtc)
{
    public ObjectAssetContentAvailability ContentAvailability { get; init; }
}
public sealed record ObjectAssetPage(IReadOnlyList<ObjectAssetPageItem> Items, ObjectAssetCursor? Next);
public sealed record ObjectAssetRecord(ObjectAssetPageItem Asset, ObjectAssetDescriptor File,
    ObjectAssetStorageReference Storage, ObjectAssetIntegrityResult? Integrity, Guid? TemporaryBindingId,
    ObjectAssetOriginReference? Origin, ObjectAssetOwnerReference? ExternalOwner)
{
    public ObjectAssetAvailabilityResult? Availability { get; init; }
}

public interface IObjectAssetPagedReader
{
    Task<ObjectAssetPage> GetOwnerPageAsync<T>(T owner, ObjectAssetSlot slot, ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default);
    Task<ObjectAssetPage> GetTemporaryPageAsync(Guid temporaryBindingId, ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default);
    Task<ObjectAssetPage> GetExternalOwnerPageAsync(ObjectAssetOwnerReference owner, ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default);
    Task<ObjectAssetPage> GetArchivePageAsync(ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default);
    Task<ObjectAssetRecord?> GetRecordAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<ObjectAssetContentResult?> OpenArchivedReadAsync(Guid assetId, CancellationToken cancellationToken = default);
}

public sealed record ObjectAssetReconciliationItem(Guid AssetId, ObjectAssetStatus Status,
    ObjectAssetBindingKind BindingKind, bool Missing, bool ExpiredTemporary);
public sealed record ObjectAssetReconciliationPage(IReadOnlyList<ObjectAssetReconciliationItem> Items, ObjectAssetCursor? Next);
public interface IObjectAssetReconciliationReader
{
    Task<ObjectAssetReconciliationPage> ReconcilePageAsync(ObjectAssetPageRequest? request = null, CancellationToken cancellationToken = default);
}
