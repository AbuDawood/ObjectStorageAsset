using System.Diagnostics.Metrics;
using Elf.ObjectStorageAsset.Domain.Entities;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal static class ObjectAssetIntegrityMetrics
{
    internal const string MeterName = "Elf.ObjectStorageAsset.Integrity";
    private static readonly Meter Meter = new(MeterName);
    internal static readonly Counter<long> Appended = Meter.CreateCounter<long>("osa.integrity.evidence.appended");
    internal static readonly Counter<long> Resolved = Meter.CreateCounter<long>("osa.integrity.evidence.resolved");
    internal static readonly Counter<long> Retried = Meter.CreateCounter<long>("osa.integrity.evidence.deferred");
    internal static readonly Counter<long> Purged = Meter.CreateCounter<long>("osa.integrity.evidence.purged");
    internal static readonly Counter<long> AssetWrites = Meter.CreateCounter<long>("osa.integrity.asset_updates");
    internal static readonly Histogram<double> VerificationDuration = Meter.CreateHistogram<double>("osa.integrity.verification.duration", "ms");
    internal static readonly Histogram<double> BatchDuration = Meter.CreateHistogram<double>("osa.integrity.evidence.batch.duration", "ms");
    private static IntegrityBacklogSnapshot? _snapshot;
    private static readonly ObservableGauge<long> Pending = Meter.CreateObservableGauge("osa.integrity.evidence.pending",
        () => Sample(s => s.Pending));
    private static readonly ObservableGauge<long> Expired = Meter.CreateObservableGauge("osa.integrity.evidence.expired",
        () => Sample(s => s.Expired));
    private static readonly ObservableGauge<double> Oldest = Meter.CreateObservableGauge("osa.integrity.evidence.oldest_pending_age", () =>
    {
        var s = Volatile.Read(ref _snapshot);
        return s is null ? [] : new[] { new Measurement<double>(s.OldestPendingAtUtc.HasValue
            ? Math.Max(0, (DateTimeOffset.UtcNow - s.OldestPendingAtUtc.Value).TotalSeconds) : 0) };
    }, "s");
    private static readonly ObservableGauge<double> SampleAge = Meter.CreateObservableGauge("osa.integrity.evidence.observation_age", () =>
    {
        var s = Volatile.Read(ref _snapshot);
        return s is null ? [] : new[] { new Measurement<double>(Math.Max(0, (DateTimeOffset.UtcNow - s.ObservedAtUtc).TotalSeconds)) };
    }, "s");
    private static IEnumerable<Measurement<long>> Sample(Func<IntegrityBacklogSnapshot, long> select)
    {
        var s = Volatile.Read(ref _snapshot);
        return s is null ? [] : [new(select(s))];
    }
    internal static void Publish(ObjectAssetMaintenanceState state)
    {
        if (state.ObservedAtUtc.HasValue && state.PendingEvidenceCount.HasValue && state.ExpiredEvidenceCount.HasValue)
            Volatile.Write(ref _snapshot, new(state.ObservedAtUtc.Value, state.PendingEvidenceCount.Value,
                state.OldestPendingEvidenceAtUtc, state.ExpiredEvidenceCount.Value));
    }
}

internal sealed record IntegrityBacklogSnapshot(DateTimeOffset ObservedAtUtc, long Pending,
    DateTimeOffset? OldestPendingAtUtc, long Expired);
