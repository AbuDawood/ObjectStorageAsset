namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Read-only catalog of resolved host bindings.
/// </summary>
public interface IObjectAssetBindingCatalog
{
    /// <summary>
    /// Returns every registered owner definition.
    /// </summary>
    IReadOnlyCollection<ObjectAssetOwnerDefinition> GetOwners();

    /// <summary>
    /// Returns the owner definition for one CLR type.
    /// </summary>
    ObjectAssetOwnerDefinition GetOwner<T>();

    /// <summary>
    /// Returns the owner definition for one CLR type.
    /// </summary>
    ObjectAssetOwnerDefinition GetOwner(Type ownerClrType);
}
