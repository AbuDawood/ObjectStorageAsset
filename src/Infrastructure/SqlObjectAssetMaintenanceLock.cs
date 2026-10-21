using System.Data;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class SqlObjectAssetMaintenanceLock(ObjectStorageAssetPersistenceOptions options, ObjectStorageAssetSchema schema)
    : IObjectAssetMaintenanceLock
{
    public async Task<IObjectAssetMaintenanceLease?> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder(options.ConnectionString) { ConnectRetryCount = 0, Pooling = false, Enlist = false };
        var connection = new SqlConnection(builder.ConnectionString);
        var resource = "OSA:Maintenance:" + schema.Name.ToLowerInvariant();
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @r;";
            command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (result == -1) { await connection.DisposeAsync(); return null; }
            if (result < 0) throw new InvalidOperationException($"OSA maintenance lock acquisition failed ({result}).");
            return new Lease(connection, resource, cancellationToken);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private sealed class Lease : IObjectAssetMaintenanceLease
    {
        private readonly SqlConnection _connection;
        private readonly string _resource;
        private readonly CancellationTokenSource _cancellation;
        public Lease(SqlConnection connection, string resource, CancellationToken ct)
        {
            _connection = connection; _resource = resource;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _connection.StateChange += OnStateChange;
        }
        public CancellationToken CancellationToken => _cancellation.Token;
        private void OnStateChange(object? sender, StateChangeEventArgs args)
        {
            if (args.CurrentState is ConnectionState.Broken or ConnectionState.Closed) _cancellation.Cancel();
        }
        public async ValueTask DisposeAsync()
        {
            _connection.StateChange -= OnStateChange;
            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    await using var command = _connection.CreateCommand();
                    command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
                    command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = _resource });
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
            finally { await _connection.DisposeAsync(); _cancellation.Dispose(); }
        }
    }
}
