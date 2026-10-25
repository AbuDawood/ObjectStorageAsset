using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal static class ObjectAssetMaintenanceMetrics
{
    private static readonly Meter Meter = new("Elf.ObjectStorageAsset.Maintenance");
    internal static readonly Counter<long> Skipped = Meter.CreateCounter<long>("osa.maintenance.skipped");
    internal static readonly Counter<long> Processed = Meter.CreateCounter<long>("osa.maintenance.processed");
    internal static readonly Counter<long> Failures = Meter.CreateCounter<long>("osa.maintenance.failures");
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("osa.maintenance.duration", "ms");
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastSuccess = new();
    private static readonly ObservableGauge<double> Age = Meter.CreateObservableGauge("osa.maintenance.seconds_since_success",
        () => LastSuccess.Select(pair => new Measurement<double>((DateTimeOffset.UtcNow - pair.Value).TotalSeconds,
            new KeyValuePair<string, object?>("task", pair.Key))), "s");
    internal static void Succeeded(string task, DateTimeOffset now) => LastSuccess[task] = now;
}
