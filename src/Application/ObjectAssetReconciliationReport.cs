namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Operational report returned by reconciliation runs.
/// </summary>
public sealed class ObjectAssetReconciliationReport
{
    public IReadOnlyList<Guid> MissingActiveAssetIds { get; init; } = [];

    public IReadOnlyList<Guid> UploadFailedAssetIds { get; init; } = [];

    public IReadOnlyList<Guid> DeleteFailedAssetIds { get; init; } = [];

    public IReadOnlyList<Guid> ExpiredTemporaryAssetIds { get; init; } = [];

    public IReadOnlyList<Guid> UnboundAssetIds { get; init; } = [];

    public IReadOnlyList<Guid> ReferencedOnlyAssetIds { get; init; } = [];

    public int PendingDeleteCount { get; init; }

    public int ActiveCount { get; init; }
}
