using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetBufferedContent : IAsyncDisposable
{
    public required Stream Content { get; init; }
    public Guid AssetId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public required string ObjectKey { get; init; }
    public ObjectKeyNamingProfile? NamingProfile { get; init; }

    public required string FileName { get; init; }

    public required string Extension { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    public required string MetadataJson { get; init; }

    public async ValueTask DisposeAsync() => await Content.DisposeAsync();
}
