using System.Linq.Expressions;
using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Resolved owner definition built from one binding contract.
/// </summary>
public sealed class ObjectAssetOwnerDefinition
{
    private Func<object, object?>? _boxedKeySelector;

    /// <summary>
    /// Stable persisted owner type code.
    /// </summary>
    public required string OwnerType { get; init; }

    /// <summary>
    /// CLR entity type represented by the binding.
    /// </summary>
    public required Type OwnerClrType { get; init; }

    /// <summary>
    /// CLR key type represented by the binding.
    /// </summary>
    public required Type OwnerKeyType { get; init; }

    /// <summary>
    /// Runtime key selector expression captured from the host contract.
    /// </summary>
    public required LambdaExpression KeySelector { get; init; }

    /// <summary>
    /// Supported slots for the owner type.
    /// </summary>
    public required IReadOnlyList<ObjectAssetSlotDefinition> Slots { get; init; }

    /// <summary>
    /// Resolves the owner key value from one runtime owner instance.
    /// </summary>
    public object? GetOwnerKey(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!OwnerClrType.IsInstanceOfType(owner))
        {
            throw new InvalidOperationException(
                $"Owner '{owner.GetType().FullName}' is not assignable to '{OwnerClrType.FullName}'.");
        }

        _boxedKeySelector ??= BuildBoxedKeySelector();
        return _boxedKeySelector(owner);
    }

    /// <summary>
    /// Returns the configured slot definition or throws if the slot is not registered.
    /// </summary>
    public ObjectAssetSlotDefinition GetRequiredSlot(ObjectAssetSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        var definition = Slots.FirstOrDefault(
            x => string.Equals(x.Name, slot.Name, StringComparison.OrdinalIgnoreCase));
        if (definition is not null)
        {
            return definition;
        }

        throw new InvalidOperationException(
            $"Slot '{slot.Name}' is not registered for owner '{OwnerType}'.");
    }

    private Func<object, object?> BuildBoxedKeySelector()
    {
        var ownerParameter = Expression.Parameter(typeof(object), "owner");
        var typedOwner = Expression.Convert(ownerParameter, OwnerClrType);
        var keyValue = Expression.Invoke(KeySelector, typedOwner);
        var boxedKeyValue = Expression.Convert(keyValue, typeof(object));

        return Expression.Lambda<Func<object, object?>>(boxedKeyValue, ownerParameter).Compile();
    }
}

/// <summary>
/// Resolved slot definition.
/// </summary>
public sealed class ObjectAssetSlotDefinition
{
    /// <summary>
    /// Stable slot name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Multiplicity rule for the slot.
    /// </summary>
    public required ObjectAssetSlotMultiplicity Multiplicity { get; init; }
}
