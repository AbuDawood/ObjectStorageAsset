namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Operational report returned by reconciliation runs.
/// </summary>
public sealed class ObjectAssetReconciliationReport
{
    public IReadOnlyList<Guid> MissingActiveAssetIds { get; init; } = [];

    public IReadOnlyList<Guid> UploadFailedAssetIds { get; init; } = [];

    public IReadOnlyList<Guid> DeleteFailedAssetIds { get; init; } = [];

    public int PendingDeleteCount { get; init; }

    public int ActiveCount { get; init; }
}
