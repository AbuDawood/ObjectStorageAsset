namespace Elf.ObjectStorageAsset.Domain.Entities;

/// <summary>Small shared task schedule, protected by OSA's maintenance application lock.</summary>
public sealed class ObjectAssetMaintenanceState
{
    public string Name { get; set; } = "";
    public DateTimeOffset NextRunAtUtc { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? LastSucceededAtUtc { get; set; }
    public DateTimeOffset? ObservedAtUtc { get; set; }
    public long? PendingEvidenceCount { get; set; }
    public DateTimeOffset? OldestPendingEvidenceAtUtc { get; set; }
    public long? ExpiredEvidenceCount { get; set; }
}
