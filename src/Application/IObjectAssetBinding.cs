using System.Linq.Expressions;

namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Defines file ownership rules for one host entity type.
/// </summary>
public interface IObjectAssetBinding<T, TKey>
{
    /// <summary>
    /// Configures the owner type, key selector, and supported slots.
    /// </summary>
    void Configure(IObjectAssetOwnerBuilder<T, TKey> builder);
}

/// <summary>
/// Builder used by host contracts to describe file ownership.
/// </summary>
public interface IObjectAssetOwnerBuilder<T, TKey>
{
    /// <summary>
    /// Sets the persisted owner type code used by ObjectStorageAsset.
    /// </summary>
    IObjectAssetOwnerBuilder<T, TKey> OwnerType(string ownerType);

    /// <summary>
    /// Sets the key selector used to resolve the owner key at runtime.
    /// </summary>
    IObjectAssetOwnerBuilder<T, TKey> Key(Expression<Func<T, TKey>> keySelector);

    /// <summary>
    /// Adds one supported file slot.
    /// </summary>
    IObjectAssetOwnerBuilder<T, TKey> Slot(ObjectAssetSlot slot);
}
