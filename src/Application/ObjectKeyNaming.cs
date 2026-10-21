using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Elf.ObjectStorageAsset.Application;

public enum ObjectKeyToken { StorageNamespace, AssetId, OriginalFileNameStem, Extension, Year, Month, Day, Hour, Minute, Second, Millisecond }
public sealed record ObjectKeyTokenDefinition(string ConfigurationName, Type ValueType, string? Format);

public static class ObjectKeyTokens
{
    public static IReadOnlyDictionary<ObjectKeyToken, ObjectKeyTokenDefinition> Definitions { get; } =
        new ReadOnlyDictionary<ObjectKeyToken, ObjectKeyTokenDefinition>(new Dictionary<ObjectKeyToken, ObjectKeyTokenDefinition>
        {
            [ObjectKeyToken.StorageNamespace] = new("StorageNamespace", typeof(string), null),
            [ObjectKeyToken.AssetId] = new("AssetId", typeof(Guid), "N"),
            [ObjectKeyToken.OriginalFileNameStem] = new("OriginalFileNameStem", typeof(string), null),
            [ObjectKeyToken.Extension] = new("Extension", typeof(string), null),
            [ObjectKeyToken.Year] = new("Year", typeof(int), "yyyy"),
            [ObjectKeyToken.Month] = new("Month", typeof(int), "MM"),
            [ObjectKeyToken.Day] = new("Day", typeof(int), "dd"),
            [ObjectKeyToken.Hour] = new("Hour", typeof(int), "HH"),
            [ObjectKeyToken.Minute] = new("Minute", typeof(int), "mm"),
            [ObjectKeyToken.Second] = new("Second", typeof(int), "ss"),
            [ObjectKeyToken.Millisecond] = new("Millisecond", typeof(int), "fff")
        });
}

public sealed class ObjectKeyTemplate
{
    private readonly StringBuilder _text = new();
    public ObjectKeyTemplate Token(ObjectKeyToken token) { _text.Append('{').Append(ObjectKeyTokens.Definitions[token].ConfigurationName).Append('}'); return this; }
    public ObjectKeyTemplate Literal(string literal) { if (literal.Contains('{') || literal.Contains('}')) throw new ArgumentException("Literal cannot contain braces."); _text.Append(literal); return this; }
    public override string ToString() => _text.ToString();
}

public sealed record ObjectKeyNamingProfile(string ProfileId, int Version, string Template, int DefinitionFormatVersion = 1)
{
    public string DefinitionHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{DefinitionFormatVersion}\n{Template}"))).ToLowerInvariant();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfileId) || ProfileId.Length > 128 || Version < 1 || DefinitionFormatVersion != 1)
            throw new InvalidOperationException("Naming profile requires a bounded id, positive version, and supported definition format.");
        if (string.IsNullOrWhiteSpace(Template) || Template.Length > 1024 || !Template.Contains("{AssetId}", StringComparison.Ordinal))
            throw new InvalidOperationException("Naming template requires the full {AssetId} token.");
        var remainder = Regex.Replace(Template, "\\{([A-Za-z]+)\\}", match =>
        {
            if (!Enum.TryParse<ObjectKeyToken>(match.Groups[1].Value, false, out var token) || !ObjectKeyTokens.Definitions.ContainsKey(token))
                throw new InvalidOperationException($"Unknown object key token '{match.Value}'.");
            return "value";
        });
        if (remainder.Contains('{') || remainder.Contains('}') || remainder.Contains('\\') || remainder.StartsWith('/')
            || remainder.Split('/').Any(s => s is "" or "." or "..") || remainder.Any(char.IsControl))
            throw new InvalidOperationException("Invalid or unsafe naming template.");
    }

    public string Render(ObjectKeyContext context)
    {
        Validate();
        var utc = context.CreatedAtUtc.ToUniversalTime();
        var extension = string.IsNullOrWhiteSpace(context.Extension) ? Path.GetExtension(context.OriginalFileName) : context.Extension;
        extension = string.IsNullOrEmpty(extension) ? "" : "." + extension.TrimStart('.').ToLowerInvariant();
        var key = Regex.Replace(Template, "\\{([A-Za-z]+)\\}", match =>
        {
            var token = Enum.Parse<ObjectKeyToken>(match.Groups[1].Value);
            return token switch
            {
                ObjectKeyToken.StorageNamespace => string.Join('/', context.StorageNamespace.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Sanitize)),
                ObjectKeyToken.AssetId => context.AssetId.ToString("N"),
                ObjectKeyToken.OriginalFileNameStem => Sanitize(Path.GetFileNameWithoutExtension(context.OriginalFileName.Replace('\\', '/'))),
                ObjectKeyToken.Extension => extension,
                _ => utc.ToString(ObjectKeyTokens.Definitions[token].Format, CultureInfo.InvariantCulture)
            };
        });
        if (key.Length > 512 || key.Split('/').Any(s => s is "" or "." or "..") || key.Contains('\\') || key.Any(char.IsControl))
            throw new InvalidOperationException("Resolved object key is unsafe or exceeds 512 characters.");
        return key;
    }

    private static string Sanitize(string value) => new(value.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? char.ToLowerInvariant(c) : '-').ToArray());
}

public sealed class ObjectKeyNamingOptions
{
    private ObjectKeyNamingProfile? _activeProfile;
    public ObjectKeyNamingProfile? ActiveProfile
    {
        get => Volatile.Read(ref _activeProfile);
        set { value?.Validate(); Volatile.Write(ref _activeProfile, value); }
    }
}

public interface IObjectAssetNamingService
{
    Task ActivateAsync(ObjectKeyNamingProfile profile, CancellationToken cancellationToken = default);
}
