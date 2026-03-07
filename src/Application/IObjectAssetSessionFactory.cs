namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Creates staged asset sessions bound to one host owner instance.
/// </summary>
public interface IObjectAssetSessionFactory
{
    IObjectAssetSession<T> For<T>(T owner);
}
