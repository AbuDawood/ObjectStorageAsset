using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Describes one logical file slot for a host entity.
/// </summary>
public sealed class ObjectAssetSlot
{
    private ObjectAssetSlot(string name, ObjectAssetSlotMultiplicity multiplicity)
    {
        Name = NormalizeName(name);
        Multiplicity = multiplicity;
    }

    /// <summary>
    /// Stable slot name used in persistence and APIs.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Multiplicity rule for the slot.
    /// </summary>
    public ObjectAssetSlotMultiplicity Multiplicity { get; }

    /// <summary>
    /// Creates a single-file slot.
    /// </summary>
    public static ObjectAssetSlot Single(string name) => new(name, ObjectAssetSlotMultiplicity.Single);

    /// <summary>
    /// Creates a multi-file slot.
    /// </summary>
    public static ObjectAssetSlot Many(string name) => new(name, ObjectAssetSlotMultiplicity.Many);

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Object asset slot name is required.");
        }

        return name.Trim();
    }
}
