namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Context required to build one provider object key.
/// </summary>
public sealed record ObjectKeyContext(
    Guid AssetId,
    string StorageNamespace,
    string OriginalFileName,
    string Extension,
    DateTimeOffset CreatedAtUtc);
