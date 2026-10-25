using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetMaintenanceService(
    ObjectStorageAssetDbContext objectStorageAssetDbContext,
    IObjectStorageProvider objectStorageProvider,
    ObjectStorageAssetRuntimeOptions runtimeOptions,
    ObjectAssetLifecycleManager lifecycleManager,
    IObjectAssetMaintenanceLock guard,
    ObjectAssetStorageResolver storageResolver, ObjectAssetIntegrityJournal journal)
    : IObjectAssetMaintenanceService, IObjectAssetMaintenanceTasks
{
    private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;
    private readonly IObjectStorageProvider _objectStorageProvider = objectStorageProvider;
    private readonly ObjectStorageAssetRuntimeOptions _runtimeOptions = runtimeOptions;
    private readonly ObjectAssetLifecycleManager _lifecycleManager = lifecycleManager;

    public Task<int> ExpireAssetsAsync(DateTimeOffset? utcNow = null, CancellationToken cancellationToken = default)
        => RunLockedAsync(ct => ExpireCoreAsync(utcNow, ct), cancellationToken);
    public Task<int> ProcessPendingDeletesAsync(CancellationToken cancellationToken = default)
        => RunLockedAsync(PendingCoreAsync, cancellationToken);
    public Task<int> RetryDeleteFailuresAsync(CancellationToken cancellationToken = default)
        => RunLockedAsync(ct => RetryCoreAsync(ct), cancellationToken);

    public Task<int> SynchronizeIntegrityEvidenceAsync(CancellationToken cancellationToken = default)
        => journal.MaintainAsync(cancellationToken);

    private async Task<int> RunLockedAsync(Func<CancellationToken, Task<int>> action, CancellationToken ct)
    {
        if (_objectStorageAssetDbContext.HasCallerTransaction)
            throw new InvalidOperationException("Maintenance must execute outside a caller-owned OSA transaction.");
        await using var lease = await guard.TryAcquireAsync(ct);
        return lease is null ? 0 : await action(lease.CancellationToken);
    }

    public async Task<int> ExpireCoreAsync(
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveUtcNow = utcNow ?? DateTimeOffset.UtcNow;
        var expiredAssets = await _objectStorageAssetDbContext.TemporaryAssets.Cast<ObjectAsset>()
            .Where(x => x.Status == ObjectAssetStatus.Active
                        && x.TemporaryBindingId.HasValue
                        && x.TemporaryBindingExpiresAtUtc.HasValue
                        && x.TemporaryBindingExpiresAtUtc <= effectiveUtcNow)
            .OrderBy(x => x.TemporaryBindingExpiresAtUtc).ThenBy(x => x.Id)
            .Take(_runtimeOptions.Maintenance.BatchSize).ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await DeleteBatchAsync(expiredAssets, _runtimeOptions.PhysicallyDeleteExpiredAssets, cancellationToken);

        return expiredAssets.Count;
    }

    public async Task<int> PendingCoreAsync(CancellationToken cancellationToken = default)
    {
        var assets = await _objectStorageAssetDbContext.ObjectAssets
            .Where(x => x.Status == ObjectAssetStatus.PendingDelete)
            .OrderBy(x => x.LastStatusChangedAtUtc).ThenBy(x => x.Id)
            .Take(_runtimeOptions.Maintenance.BatchSize).ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await DeleteBatchAsync(assets, null, cancellationToken);

        return assets.Count;
    }

    public async Task<int> RetryCoreAsync(CancellationToken cancellationToken = default, DateTimeOffset? failedBefore = null)
    {
        var now = DateTimeOffset.UtcNow;
        var assets = await _objectStorageAssetDbContext.ObjectAssets
            .Where(x => x.Status == ObjectAssetStatus.DeleteFailed && !x.DeleteRetrySuspended
                && (!failedBefore.HasValue || x.LastStatusChangedAtUtc < failedBefore)
                && (!x.NextDeleteAttemptAtUtc.HasValue || x.NextDeleteAttemptAtUtc <= now))
            .OrderBy(x => x.NextDeleteAttemptAtUtc).ThenBy(x => x.Id)
            .Take(_runtimeOptions.Maintenance.BatchSize).ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await DeleteBatchAsync(assets, null, cancellationToken);

        return assets.Count;
    }

    private async Task DeleteBatchAsync(List<ObjectAsset> assets, bool? physicallyDelete, CancellationToken ct)
    {
        if (assets.Count == 0) return;
        bool ShouldDelete(ObjectAsset asset) => physicallyDelete ?? asset.PhysicalDeletionRequested
            ?? (_runtimeOptions.PhysicallyDeleteExpiredAssets && _runtimeOptions.PhysicallyDeleteRequestedAssets);
        foreach (var asset in assets) asset.MarkPendingDelete(DateTimeOffset.UtcNow, ShouldDelete(asset));
        await _objectStorageAssetDbContext.SaveChangesAsync(ct);
        var outcomes = new System.Collections.Concurrent.ConcurrentDictionary<Guid, string?>();
        await Parallel.ForEachAsync(assets, new ParallelOptions { MaxDegreeOfParallelism = _runtimeOptions.Maintenance.ProviderConcurrency, CancellationToken = ct }, async (asset, token) =>
        {
            try
            {
                if (ShouldDelete(asset) && asset.OwnershipMode == ObjectAssetOwnershipMode.Managed)
                    await storageResolver.Resolve(asset.StorageAuthorityId).DeleteObjectAsync(new ObjectStorageDeleteRequest
                    { BucketName = asset.BucketName, ObjectKey = asset.ObjectKey, VersionId = asset.ProviderVersionId }, token);
                outcomes[asset.Id] = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { outcomes[asset.Id] = ex.GetType().Name; }
        });
        foreach (var asset in assets)
        {
            if (outcomes[asset.Id] is { } error)
            {
                asset.MarkDeleteFailed(DateTimeOffset.UtcNow, error);
                var seconds = Math.Min(_runtimeOptions.Maintenance.MaximumRetryDelay.TotalSeconds,
                    _runtimeOptions.Maintenance.InitialRetryDelay.TotalSeconds * Math.Pow(2, Math.Min(asset.DeleteAttemptCount, 20)) * (0.8 + Random.Shared.NextDouble() * 0.4));
                asset.ScheduleDeleteRetry(DateTimeOffset.UtcNow.AddSeconds(seconds), _runtimeOptions.Maintenance.MaxAttempts);
            }
            else asset.MarkDeleted(DateTimeOffset.UtcNow, ShouldDelete(asset) && asset.OwnershipMode == ObjectAssetOwnershipMode.Managed);
        }
        await _objectStorageAssetDbContext.SaveChangesAsync(ct);
    }

    public async Task<ObjectAssetReconciliationReport> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var uploadFailedAssetIds = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .Where(x => x.Status == ObjectAssetStatus.UploadFailed)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var deleteFailedAssetIds = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .Where(x => x.Status == ObjectAssetStatus.DeleteFailed)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var pendingDeleteCount = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .CountAsync(x => x.Status == ObjectAssetStatus.PendingDelete, cancellationToken)
            .ConfigureAwait(false);

        var expiredTemporaryAssetIds = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .Where(x => x.Status == ObjectAssetStatus.Active
                        && x.TemporaryBindingId.HasValue
                        && x.TemporaryBindingExpiresAtUtc.HasValue
                        && x.TemporaryBindingExpiresAtUtc <= utcNow)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var unboundAssetIds = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .Where(x => x.Status == ObjectAssetStatus.Active
                        && x.BindingKind == ObjectAssetBindingKind.LegacyUnresolved
                        && x.OwnerKeyKind == ObjectAssetOwnerKeyKind.Unassigned
                        && !x.TemporaryBindingId.HasValue)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var referencedOnlyAssetIds = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .Where(x => x.Status == ObjectAssetStatus.Active
                        && x.OwnershipMode == ObjectAssetOwnershipMode.Referenced)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var externalOwnerIds = await _objectStorageAssetDbContext.PermanentAssets.AsNoTracking()
            .Where(x => x.Status == ObjectAssetStatus.Active && x.BindingKind == ObjectAssetBindingKind.ExternalOwner)
            .Select(x => x.Id).ToArrayAsync(cancellationToken);

        var activeAssets = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .Where(x => x.Status == ObjectAssetStatus.Active)
            .Select(x => new ActiveAssetProjection(
                x.Id,
                x.StorageAuthorityId,
                x.BucketName,
                x.ObjectKey,
                x.ProviderVersionId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var missingActiveAssetIds = new List<Guid>();
        foreach (var activeAsset in activeAssets)
        {
            var stat = await storageResolver.Resolve(activeAsset.StorageAuthorityId).StatObjectAsync(
                    new ObjectStorageStatRequest
                    {
                        BucketName = activeAsset.BucketName,
                        ObjectKey = activeAsset.ObjectKey,
                        VersionId = activeAsset.ProviderVersionId
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (stat is null)
            {
                missingActiveAssetIds.Add(activeAsset.Id);
            }
        }

        return new ObjectAssetReconciliationReport
        {
            MissingActiveAssetIds = missingActiveAssetIds,
            UploadFailedAssetIds = uploadFailedAssetIds,
            DeleteFailedAssetIds = deleteFailedAssetIds,
            ExpiredTemporaryAssetIds = expiredTemporaryAssetIds,
            UnboundAssetIds = unboundAssetIds,
            ExternalOwnerAssetIds = externalOwnerIds,
            ReferencedOnlyAssetIds = referencedOnlyAssetIds,
            PendingDeleteCount = pendingDeleteCount,
            ActiveCount = activeAssets.Length
        };
    }

    private sealed record ActiveAssetProjection(
        Guid Id,
        Guid StorageAuthorityId,
        string BucketName,
        string ObjectKey,
        string? ProviderVersionId);
}
