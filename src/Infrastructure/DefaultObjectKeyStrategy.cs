using System.Text;
using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.Infrastructure;

/// <summary>
/// Default object key strategy used by OSA when the host does not override it.
/// </summary>
public sealed class DefaultObjectKeyStrategy : IObjectKeyStrategy
{
    public string CreateObjectKey(ObjectKeyContext context)
    {
        if (context.AssetId == Guid.Empty)
        {
            throw new InvalidOperationException("Object key creation requires a non-empty asset id.");
        }

        var storageNamespace = NormalizeNamespace(context.StorageNamespace);
        if (string.IsNullOrWhiteSpace(storageNamespace))
        {
            throw new InvalidOperationException("Object key creation requires a storage namespace.");
        }

        var extension = NormalizeExtension(context.Extension, context.OriginalFileName);
        var createdAtUtc = context.CreatedAtUtc.ToUniversalTime();

        return $"{storageNamespace}/{createdAtUtc:yyyy}/{createdAtUtc:MM}/{context.AssetId:N}{extension}";
    }

    private static string NormalizeNamespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var parts = value.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSegment)
            .Where(x => x.Length > 0);

        return string.Join('/', parts);
    }

    private static string NormalizeSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }

    private static string NormalizeExtension(string extension, string originalFileName)
    {
        var candidate = string.IsNullOrWhiteSpace(extension)
            ? Path.GetExtension(originalFileName)
            : extension;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        return candidate.StartsWith(".", StringComparison.Ordinal)
            ? candidate.ToLowerInvariant()
            : $".{candidate.ToLowerInvariant()}";
    }
}
