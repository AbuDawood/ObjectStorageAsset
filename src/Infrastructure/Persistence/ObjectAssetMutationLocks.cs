using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

public sealed partial class ObjectStorageAssetDbContext
{
    /// <summary>
    /// Reserves a known set of asset identities in SQL key order, before reading payloads for mutation.
    /// Caller-owned transactions spanning several operations should reserve their complete set up front.
    /// </summary>
    public Task ReserveAssetsForUpdateAsync(IEnumerable<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Asset reservations require an explicit OSA transaction.");
        return LockAssetIdentitiesAsync(assetIds, exclusive: false, cancellationToken);
    }

    internal Task ReserveAssetsInCallerTransactionAsync(IEnumerable<Guid> assetIds, CancellationToken ct)
        => Database.CurrentTransaction is null ? Task.CompletedTask : ReserveAssetsForUpdateAsync(assetIds, ct);

    private async Task<IDbContextTransaction?> BeginAssetSaveAsync(IEnumerable<Guid> assetIds, CancellationToken ct)
    {
        var ids = assetIds.Distinct().ToArray();
        if (Database.CurrentTransaction is not null)
        {
            await LockAssetIdentitiesAsync(ids, exclusive: true, ct).ConfigureAwait(false);
            return null; // The caller owns commit, rollback and any whole-transaction retry.
        }
        for (var attempt = 0; ; attempt++)
        {
            var transaction = await Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                await LockAssetIdentitiesAsync(ids, exclusive: true, ct).ConfigureAwait(false);
                return transaction;
            }
            catch (Exception ex) when (ObjectAssetSqlConcurrency.IsDeadlock(ex) && attempt < 2)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                // Only lock acquisition is replayed: no payload writes or provider calls have run.
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1) + Random.Shared.Next(25)), ct).ConfigureAwait(false);
            }
            catch
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private async Task LockAssetIdentitiesAsync(IEnumerable<Guid> assetIds, bool exclusive, CancellationToken ct)
    {
        var ids = assetIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        var table = "[" + _schemaName.Replace("]", "]]", StringComparison.Ordinal) + "].[ObjectAssetIdentity]";
        var hint = exclusive ? "XLOCK,HOLDLOCK" : "UPDLOCK,HOLDLOCK";
        // ORDER BY controls cursor fetch order; each fetch locks one indexed identity before the next.
        // Sorting an IN-list alone does not establish SQL Server's lock acquisition order.
        // One parameterized roundtrip also avoids a client roundtrip per asset and the 2100-parameter limit.
        var sql = $$"""
            SET NOCOUNT ON;
            DECLARE @assetId uniqueidentifier, @lockedId uniqueidentifier;
            DECLARE osa_asset_locks CURSOR LOCAL FAST_FORWARD FOR
                SELECT [id] FROM OPENJSON(@assetIds) WITH ([id] uniqueidentifier '$') ORDER BY [id];
            OPEN osa_asset_locks;
            FETCH NEXT FROM osa_asset_locks INTO @assetId;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SELECT @lockedId = [AssetId] FROM {{table}} WITH ({{hint}}) WHERE [AssetId] = @assetId;
                FETCH NEXT FROM osa_asset_locks INTO @assetId;
            END;
            CLOSE osa_asset_locks;
            DEALLOCATE osa_asset_locks;
            """;
#pragma warning disable EF1002
        await Database.ExecuteSqlRawAsync(sql,
            [new SqlParameter("@assetIds", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(ids) }], ct).ConfigureAwait(false);
#pragma warning restore EF1002
    }
}

internal static class ObjectAssetSqlConcurrency
{
    internal static bool IsDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 }) return true;
        return false;
    }
}
