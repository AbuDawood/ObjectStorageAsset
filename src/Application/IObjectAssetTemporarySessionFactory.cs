namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Creates immediate-write temporary upload sessions.
/// </summary>
public interface IObjectAssetTemporarySessionFactory
{
    IObjectAssetTemporarySession<T> For<T>(
        Guid temporaryBindingId,
        DateTimeOffset? temporaryBindingExpiresAtUtc = null);
}
