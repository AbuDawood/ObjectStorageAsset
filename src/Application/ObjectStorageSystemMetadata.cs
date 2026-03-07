namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Reserved metadata keys stamped onto stored objects.
/// </summary>
public static class ObjectStorageSystemMetadata
{
    public const string AssetIdKey = "osa-asset-id";
    public const string StorageNamespaceKey = "osa-storage-namespace";
    public const string Sha256Key = "osa-sha256";

    public static IReadOnlyDictionary<string, string> Create(
        Guid assetId,
        string storageNamespace,
        string? sha256)
    {
        if (assetId == Guid.Empty)
        {
            throw new InvalidOperationException("Object storage metadata requires a non-empty asset id.");
        }

        if (string.IsNullOrWhiteSpace(storageNamespace))
        {
            throw new InvalidOperationException("Object storage metadata requires a storage namespace.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AssetIdKey] = assetId.ToString("N"),
            [StorageNamespaceKey] = storageNamespace.Trim()
        };

        if (!string.IsNullOrWhiteSpace(sha256))
        {
            metadata[Sha256Key] = sha256.Trim();
        }

        return metadata;
    }
}
