using System.Text.Json;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal static class ObjectAssetMetadataBagSerializer
{
    public static IReadOnlyDictionary<string, string> Normalize(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return Empty;
        }

        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new InvalidOperationException("Asset metadata keys are required.");
            }

            var key = pair.Key.Trim().ToLowerInvariant();
            if (normalized.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Asset metadata key '{pair.Key}' is duplicated after normalization.");
            }

            normalized[key] = pair.Value?.Trim() ?? string.Empty;
        }

        return normalized;
    }

    public static IReadOnlyDictionary<string, string> Deserialize(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Empty;
        }

        var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
        return Normalize(dictionary);
    }

    public static string Serialize(IReadOnlyDictionary<string, string>? metadata)
    {
        var normalized = Normalize(metadata);
        return JsonSerializer.Serialize(normalized);
    }

    private static IReadOnlyDictionary<string, string> Empty { get; } =
        new Dictionary<string, string>();
}
