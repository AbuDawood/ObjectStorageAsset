namespace Elf.ObjectStorageAsset.Application;

public sealed class ObjectAssetMaintenanceOptions
{
    public int BatchSize { get; set; } = 200;
    public int ProviderConcurrency { get; set; } = 4;
    public int MaxAttempts { get; set; } = 10;
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromHours(12);
    public TimeSpan? ExpiryInterval { get; set; }
    public TimeSpan? PendingDeleteInterval { get; set; }
    public TimeSpan? RetryInterval { get; set; }
}

public sealed class ObjectAssetBufferOptions
{
    public long MemoryThresholdBytes { get; set; } = 4 * 1024 * 1024;
    public long MaximumMemoryBytes { get; set; } = 16 * 1024 * 1024;
    public int MaximumStagedFiles { get; set; } = 1024;
    public long MaximumFileBytes { get; set; } = 5L * 1024 * 1024 * 1024;
    public long MaximumStagedBytes { get; set; } = 20L * 1024 * 1024 * 1024;
    public int MaximumConcurrentStaging { get; set; } = 4;
    public string TemporaryDirectory { get; set; } = Path.GetTempPath();
}

public interface IObjectStorageCopyProvider
{
    Task<ObjectStorageStatResult> CopyToAsync(ObjectStorageGetRequest request, Stream destination, CancellationToken cancellationToken = default);
}
