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
    public TimeSpan? IntegrityEvidenceInterval { get; set; }
    public ObjectAssetEvidenceMaintenanceOptions Evidence { get; set; } = new();
}

public sealed class ObjectAssetEvidenceMaintenanceOptions
{
    public int BatchSize { get; set; } = 200;
    public int PurgeBatchSize { get; set; } = 1000;
    public int MaximumBatchesPerRun { get; set; } = 10;
    /// <summary>Per-phase budget checked between asset transactions/purge commands; in-flight work may finish afterward.</summary>
    public TimeSpan TimeBudget { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromMinutes(5);
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
