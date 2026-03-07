using System.Security.Cryptography;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetBufferedContent
{
    public required byte[] Bytes { get; init; }

    public required string FileName { get; init; }

    public required string Extension { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public static async Task<ObjectAssetBufferedContent> CreateAsync(
        Stream content,
        string fileName,
        string? contentType,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Object asset staging requires a file name.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        var bytes = buffer.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        return new ObjectAssetBufferedContent
        {
            Bytes = bytes,
            FileName = fileName.Trim(),
            Extension = Path.GetExtension(fileName)?.Trim() ?? string.Empty,
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType.Trim(),
            SizeBytes = bytes.LongLength,
            Sha256 = sha256,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}
