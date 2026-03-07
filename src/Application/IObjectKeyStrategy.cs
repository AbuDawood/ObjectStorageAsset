namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Produces deterministic object keys for newly created assets.
/// </summary>
public interface IObjectKeyStrategy
{
    string CreateObjectKey(ObjectKeyContext context);
}
