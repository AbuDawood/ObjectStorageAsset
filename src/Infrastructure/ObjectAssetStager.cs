using System.Buffers;
using System.Security.Cryptography;
using Elf.ObjectStorageAsset.Application;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetStager(ObjectStorageAssetRuntimeOptions options, IObjectKeyStrategy keyStrategy)
{
    private readonly SemaphoreSlim _gate = new(options.Buffering.MaximumConcurrentStaging);
    private long _retainedBytes;
    private long _memoryBytes;
    private int _retainedFiles;

    public async Task<(Stream Content, string? ObjectVersion)> DownloadAsync(IObjectStorageProvider provider, ObjectStorageGetRequest request, CancellationToken ct)
    {
        if (provider is not IObjectStorageCopyProvider copier)
        {
            var result = await provider.GetObjectAsync(request, ct);
            await using var source = result.Content;
            return (await StageStreamAsync(source, ct), result.VersionId ?? request.VersionId);
        }
        await _gate.WaitAsync(ct);
        Stream? destination = null;
        long retained = 0;
        var ownsSlot = false;
        try
        {
            if (Interlocked.Increment(ref _retainedFiles) > options.Buffering.MaximumStagedFiles)
            { Interlocked.Decrement(ref _retainedFiles); throw new IOException("OSA staged-file count limit exceeded."); }
            ownsSlot = true;
            Directory.CreateDirectory(options.Buffering.TemporaryDirectory);
            var file = new FileStream(Path.Combine(options.Buffering.TemporaryDirectory, $"osa-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            destination = new RetainedStream(file, () => { Interlocked.Add(ref _retainedBytes, -retained); Interlocked.Decrement(ref _retainedFiles); }, count =>
            {
                if (retained + count > options.Buffering.MaximumFileBytes) throw new IOException("OSA per-file staging limit exceeded.");
                retained += count;
                if (Interlocked.Add(ref _retainedBytes, count) > options.Buffering.MaximumStagedBytes)
                    throw new IOException("OSA aggregate staging limit exceeded.");
            });
            var metadata = await copier.CopyToAsync(request, destination, ct);
            destination.Position = 0;
            ((RetainedStream)destination).Seal();
            return (destination, metadata.VersionId ?? request.VersionId);
        }
        catch
        {
            if (destination is not null) await destination.DisposeAsync();
            else if (ownsSlot) Interlocked.Decrement(ref _retainedFiles);
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<ObjectAssetBufferedContent> StageAsync(Stream source, string fileName, string? contentType,
        DateTimeOffset? expiresAtUtc, IReadOnlyDictionary<string, string>? metadata, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(fileName)) throw new InvalidOperationException("Staging requires a filename.");
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var extension = Path.GetExtension(fileName);
        var profile = keyStrategy is DefaultObjectKeyStrategy ? options.Naming.ActiveProfile : null;
        var context = new ObjectKeyContext(id, options.DefaultStorageNamespace, fileName, extension, created);
        var key = keyStrategy is DefaultObjectKeyStrategy
            ? profile?.Render(context) ?? new DefaultObjectKeyStrategy().CreateObjectKey(context)
            : keyStrategy.CreateObjectKey(context);
        var bag = ObjectAssetMetadataBagSerializer.Normalize(metadata);
        var stream = await StageStreamAsync(source, ct);
        try
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            stream.Position = 0;
            return new ObjectAssetBufferedContent
            {
                AssetId = id,
                CreatedAtUtc = created,
                ObjectKey = key,
                NamingProfile = profile,
                Content = stream,
                FileName = fileName.Trim(),
                Extension = extension,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
                SizeBytes = stream.Length,
                Sha256 = hash,
                ExpiresAtUtc = expiresAtUtc,
                Metadata = bag,
                MetadataJson = ObjectAssetMetadataBagSerializer.Serialize(bag)
            };
        }
        catch { await stream.DisposeAsync(); throw; }
    }

    public async Task<Stream> StageStreamAsync(Stream source, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        Stream? stream = null;
        long retained = 0;
        long memoryReserved = 0;
        var ownsSlot = false;
        try
        {
            if (Interlocked.Increment(ref _retainedFiles) > options.Buffering.MaximumStagedFiles)
            { Interlocked.Decrement(ref _retainedFiles); throw new IOException("OSA staged-file count limit exceeded."); }
            ownsSlot = true;
            Directory.CreateDirectory(options.Buffering.TemporaryDirectory);
            stream = new MemoryStream();
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                if (retained + read > options.Buffering.MaximumFileBytes) throw new IOException("OSA per-file staging limit exceeded.");
                var total = Interlocked.Add(ref _retainedBytes, read);
                retained += read;
                if (total > options.Buffering.MaximumStagedBytes) throw new IOException("OSA aggregate staging limit exceeded.");
                if (stream is MemoryStream memory)
                {
                    var capacity = retained <= memory.Capacity ? memory.Capacity
                        : Math.Min(Math.Min(options.Buffering.MemoryThresholdBytes, int.MaxValue), Math.Max(retained, Math.Max(256L, memory.Capacity * 2L)));
                    var increment = Math.Max(0, capacity - memory.Capacity);
                    var fits = retained <= capacity;
                    if (fits && increment > 0)
                    {
                        if (Interlocked.Add(ref _memoryBytes, increment) <= options.Buffering.MaximumMemoryBytes)
                        { memoryReserved += increment; memory.Capacity = (int)capacity; }
                        else { Interlocked.Add(ref _memoryBytes, -increment); fits = false; }
                    }
                    if (!fits)
                    {
                        var path = Path.Combine(options.Buffering.TemporaryDirectory, $"osa-{Guid.NewGuid():N}.tmp");
                        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536,
                            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                        try { memory.Position = 0; await memory.CopyToAsync(file, ct); }
                        catch { await file.DisposeAsync(); throw; }
                        memory.Dispose(); stream = file;
                        Interlocked.Add(ref _memoryBytes, -memoryReserved); memoryReserved = 0;
                    }
                }
                await stream.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            stream.Position = 0;
            var result = new RetainedStream(stream, () =>
            { Interlocked.Add(ref _retainedBytes, -retained); Interlocked.Add(ref _memoryBytes, -memoryReserved); Interlocked.Decrement(ref _retainedFiles); });
            result.Seal();
            return result;
        }
        catch
        {
            if (stream is not null) await stream.DisposeAsync();
            Interlocked.Add(ref _retainedBytes, -retained);
            Interlocked.Add(ref _memoryBytes, -memoryReserved);
            if (ownsSlot) Interlocked.Decrement(ref _retainedFiles);
            throw;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); _gate.Release(); }
    }

    private sealed class RetainedStream(Stream inner, Action release, Action<int>? reserve = null) : Stream
    {
        private int _disposed;
        private bool _writable = true;
        public void Seal() => _writable = false;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => _writable && inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override void Write(byte[] buffer, int offset, int count) { if (!_writable) throw new NotSupportedException(); reserve?.Invoke(count); inner.Write(buffer, offset, count); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { if (!_writable) throw new NotSupportedException(); reserve?.Invoke(buffer.Length); return inner.WriteAsync(buffer, ct); }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) { try { if (disposing) inner.Dispose(); } finally { release(); } }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) { try { await inner.DisposeAsync(); } finally { release(); } }
            GC.SuppressFinalize(this);
        }
    }
}

