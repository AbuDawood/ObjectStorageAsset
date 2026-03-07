namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Runs operational maintenance against object asset lifecycle state.
/// </summary>
public interface IObjectAssetMaintenanceService
{
    Task<int> ExpireAssetsAsync(
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default);

    Task<int> ProcessPendingDeletesAsync(CancellationToken cancellationToken = default);

    Task<int> RetryDeleteFailuresAsync(CancellationToken cancellationToken = default);

    Task<ObjectAssetReconciliationReport> ReconcileAsync(CancellationToken cancellationToken = default);
}
