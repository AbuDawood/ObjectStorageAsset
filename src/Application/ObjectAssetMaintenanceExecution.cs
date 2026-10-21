namespace Elf.ObjectStorageAsset.Application;

public interface IObjectAssetMaintenanceRunner
{
    Task RunOnceAsync(CancellationToken cancellationToken = default);
}

public interface IObjectAssetMaintenanceLease : IAsyncDisposable
{
    CancellationToken CancellationToken { get; }
}

public interface IObjectAssetMaintenanceLock
{
    Task<IObjectAssetMaintenanceLease?> TryAcquireAsync(CancellationToken cancellationToken = default);
}
