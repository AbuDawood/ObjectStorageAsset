using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetMaintenanceService(
    ObjectStorageAssetDbContext objectStorageAssetDbContext,
    IObjectStorageProvider objectStorageProvider,
    ObjectStorageAssetRuntimeOptions runtimeOptions)
    : IObjectAssetMaintenanceService
{
    private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;
    private readonly IObjectStorageProvider _objectStorageProvider = objectStorageProvider;
    private readonly ObjectStorageAssetRuntimeOptions _runtimeOptions = runtimeOptions;

    public async Task<int> ExpireAssetsAsync(
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveUtcNow = utcNow ?? DateTimeOffset.UtcNow;
        var expiredAssets = await _objectStorageAssetDbContext.ObjectAssets
            .Where(x => x.Status == ObjectAssetStatus.Active
                        && x.ExpiresAtUtc.HasValue
                        && x.ExpiresAtUtc <= effectiveUtcNow)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var asset in expiredAssets)
        {
            if (_runtimeOptions.PhysicallyDeleteExpiredAssets)
            {
                await DeletePhysicallyAsync(asset, rethrowOnFailure: false, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                asset.MarkDeleted(effectiveUtcNow, physicallyDeleted: false);
                await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return expiredAssets.Count;
    }

    public async Task<int> ProcessPendingDeletesAsync(CancellationToken cancellationToken = default)
    {
        var assets = await _objectStorageAssetDbContext.ObjectAssets
            .Where(x => x.Status == ObjectAssetStatus.PendingDelete)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var asset in assets)
        {
            await DeletePhysicallyAsync(asset, rethrowOnFailure: false, cancellationToken).ConfigureAwait(false);
        }

        return assets.Count;
    }

    public async Task<int> RetryDeleteFailuresAsync(CancellationToken cancellationToken = default)
    {
        var assets = await _objectStorageAssetDbContext.ObjectAssets
            .Where(x => x.Status == ObjectAssetStatus.DeleteFailed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var asset in assets)
        {
            await DeletePhysicallyAsync(asset, rethrowOnFailure: false, cancellationToken).ConfigureAwait(false);
        }

        return assets.Count;
    }

    public async Task<ObjectAssetReconciliationReport> ReconcileAsync(CancellationToken cancellationToken = default)
    {
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

        var activeAssets = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .Where(x => x.Status == ObjectAssetStatus.Active)
            .Select(x => new ActiveAssetProjection(
                x.Id,
                x.BucketName,
                x.ObjectKey,
                x.ProviderVersionId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var missingActiveAssetIds = new List<Guid>();
        foreach (var activeAsset in activeAssets)
        {
            var stat = await _objectStorageProvider.StatObjectAsync(
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
            PendingDeleteCount = pendingDeleteCount,
            ActiveCount = activeAssets.Length
        };
    }

    private async Task DeletePhysicallyAsync(
        ObjectAsset asset,
        bool rethrowOnFailure,
        CancellationToken cancellationToken)
    {
        if (asset.Status != ObjectAssetStatus.PendingDelete)
        {
            asset.MarkPendingDelete(DateTimeOffset.UtcNow);
            await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await _objectStorageProvider.DeleteObjectAsync(
                    new ObjectStorageDeleteRequest
                    {
                        BucketName = asset.BucketName,
                        ObjectKey = asset.ObjectKey,
                        VersionId = asset.ProviderVersionId
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            asset.MarkDeleted(DateTimeOffset.UtcNow, physicallyDeleted: true);
            await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            asset.MarkDeleteFailed(DateTimeOffset.UtcNow, ex.Message);
            await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (rethrowOnFailure)
            {
                throw;
            }
        }
    }

    private sealed record ActiveAssetProjection(
        Guid Id,
        string BucketName,
        string ObjectKey,
        string? ProviderVersionId);
}
