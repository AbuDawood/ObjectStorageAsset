namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Finalizes one temporary binding to a concrete owner instance.
/// </summary>
public sealed class ObjectAssetTemporaryBindingFinalizationRequest<T>
{
    public required Guid TemporaryBindingId { get; init; }

    public required T Owner { get; init; }
}
