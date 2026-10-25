using System.Transactions;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed record ObjectAssetContentIdentity(Guid Id, Guid StorageAuthorityId, string BucketName,
    string ObjectKey, string? ProviderVersionId, string Sha256, long SizeBytes)
{
    internal static ObjectAssetContentIdentity From(ObjectAsset asset) => new(asset.Id, asset.StorageAuthorityId,
        asset.BucketName, asset.ObjectKey, asset.ProviderVersionId, asset.Sha256, asset.SizeBytes);
}

internal sealed class ObjectAssetAvailabilityStore(ObjectStorageAssetPersistenceOptions persistence, ObjectStorageAssetSchema schema)
{
    private ObjectStorageAssetDbContext Create()
    {
        var connection = new SqlConnectionStringBuilder(persistence.ConnectionString)
        { Enlist = false, Pooling = true, ConnectTimeout = 5 };
        return new(new DbContextOptionsBuilder<ObjectStorageAssetDbContext>()
            .UseSqlServer(connection.ConnectionString, sql => sql.CommandTimeout(5)).Options, schema);
    }

    internal static ObjectAssetAvailabilityResult Result(ObjectAssetContentState state) => new(state.AssetId,
        state.Availability, state.CheckedAtUtc, state.MissingSinceUtc, state.MissingReason);

    internal Task<ObjectAssetAvailabilityResult> GetAsync(ObjectAsset asset, CancellationToken ct)
        => GetAsync(ObjectAssetContentIdentity.From(asset), ct);

    internal async Task<ObjectAssetAvailabilityResult> GetAsync(ObjectAssetContentIdentity asset, CancellationToken ct)
    {
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        await using var db = Create();
        var state = await db.ContentStates.AsNoTracking().SingleOrDefaultAsync(x => x.AssetId == asset.Id, ct).ConfigureAwait(false);
        suppression.Complete();
        return state is not null && state.StorageAuthorityId == asset.StorageAuthorityId && state.BucketName == asset.BucketName
            && state.ObjectKey == asset.ObjectKey && state.RequestedVersion == asset.ProviderVersionId
            && state.ExpectedHash == asset.Sha256 && state.ExpectedSize == asset.SizeBytes
            ? Result(state) : new(asset.Id, ObjectAssetContentAvailability.Unknown);
    }

    internal Task<ObjectAssetAvailabilityResult> RecordAsync(ObjectAsset asset, ObjectAssetContentAvailability availability,
        DateTimeOffset checkedAtUtc, ObjectAssetMissingReason? reason, CancellationToken ct)
        => RecordAsync(ObjectAssetContentIdentity.From(asset), availability, checkedAtUtc, reason, ct);

    internal async Task<ObjectAssetAvailabilityResult> RecordAsync(ObjectAssetContentIdentity asset, ObjectAssetContentAvailability availability,
        DateTimeOffset checkedAtUtc, ObjectAssetMissingReason? reason, CancellationToken ct)
    {
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        await using var db = Create();
        var missing = availability == ObjectAssetContentAvailability.Missing;
        // Atomic timestamp comparison prevents a slow, older observation overwriting a newer recheck.
        for (var attempt = 0; ; attempt++)
        {
            var updated = await db.ContentStates.Where(x => x.AssetId == asset.Id && x.CheckedAtUtc <= checkedAtUtc)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(x => x.MissingSinceUtc, x => missing
                        ? x.Availability == ObjectAssetContentAvailability.Missing && x.StorageAuthorityId == asset.StorageAuthorityId
                            && x.BucketName == asset.BucketName && x.ObjectKey == asset.ObjectKey && x.RequestedVersion == asset.ProviderVersionId
                            && x.ExpectedHash == asset.Sha256 && x.ExpectedSize == asset.SizeBytes ? x.MissingSinceUtc ?? checkedAtUtc : checkedAtUtc
                        : (DateTimeOffset?)null)
                    .SetProperty(x => x.StorageAuthorityId, asset.StorageAuthorityId)
                    .SetProperty(x => x.BucketName, asset.BucketName).SetProperty(x => x.ObjectKey, asset.ObjectKey)
                    .SetProperty(x => x.RequestedVersion, asset.ProviderVersionId).SetProperty(x => x.ExpectedHash, asset.Sha256)
                    .SetProperty(x => x.ExpectedSize, asset.SizeBytes).SetProperty(x => x.Availability, availability)
                    .SetProperty(x => x.CheckedAtUtc, checkedAtUtc).SetProperty(x => x.MissingReason, reason), ct).ConfigureAwait(false);
            if (updated != 0 || await db.ContentStates.AnyAsync(x => x.AssetId == asset.Id, ct).ConfigureAwait(false)) break;
            db.ContentStates.Add(new()
            {
                AssetId = asset.Id, StorageAuthorityId = asset.StorageAuthorityId, BucketName = asset.BucketName,
                ObjectKey = asset.ObjectKey, RequestedVersion = asset.ProviderVersionId, ExpectedHash = asset.Sha256,
                ExpectedSize = asset.SizeBytes, Availability = availability, CheckedAtUtc = checkedAtUtc,
                MissingSinceUtc = missing ? checkedAtUtc : null, MissingReason = reason
            });
            try { await db.SaveChangesAsync(ct).ConfigureAwait(false); break; }
            catch (DbUpdateException ex) when (attempt == 0 && ex.InnerException is SqlException { Number: 2601 or 2627 })
            { db.ChangeTracker.Clear(); } // Concurrent first observations: retry the atomic update.
        }
        suppression.Complete();
        return new(asset.Id, availability, checkedAtUtc, missing ? checkedAtUtc : null, reason);
    }
}
