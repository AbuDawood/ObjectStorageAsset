using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Elf.ObjectStorageAsset.Domain.Entities;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class MaintenanceRunnerTests
{
    [Test]
    public async Task FailedTask_ShouldNotBlockSubsequentTasksAndShouldRecoverNextCycle()
    {
        var tasks = new Tasks { FailExpiry = true }; var clock = new Clock();
        var resolutions = 0;
        using var provider = new ServiceCollection().AddSingleton<IObjectAssetMaintenanceSchedule, MemorySchedule>()
            .AddScoped<IObjectAssetMaintenanceTasks>(_ => { resolutions++; return tasks; }).BuildServiceProvider();
        var runner = new ObjectAssetMaintenanceRunner(provider.GetRequiredService<IServiceScopeFactory>(), new Gate(),
            new(), NullLogger<ObjectAssetMaintenanceRunner>.Instance, clock);
        await runner.RunOnceAsync();
        tasks.Calls.Should().Equal("expiry", "pending", "retry", "integrity", "observation");
        resolutions.Should().Be(5, "each task must receive a fresh scope");
        tasks.FailExpiry = false; clock.Now = clock.Now.AddMinutes(16);
        await runner.RunOnceAsync();
        tasks.Calls.Should().Equal("expiry", "pending", "retry", "integrity", "observation", "expiry", "pending", "retry", "integrity", "observation");
        resolutions.Should().Be(10);
    }

    [Test]
    public async Task ConcurrentRunners_ShouldSkipWhenOneHoldsLock()
    {
        var tasks = new Tasks { BlockExpiry = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var provider = new ServiceCollection().AddSingleton<IObjectAssetMaintenanceSchedule, MemorySchedule>()
            .AddScoped<IObjectAssetMaintenanceTasks>(_ => tasks).BuildServiceProvider();
        var guard = new Gate(); var clock = new Clock();
        ObjectAssetMaintenanceRunner Create() => new(provider.GetRequiredService<IServiceScopeFactory>(), guard, new(), NullLogger<ObjectAssetMaintenanceRunner>.Instance, clock);
        var first = Create().RunOnceAsync();
        await tasks.Started.Task;
        await Create().RunOnceAsync();
        tasks.Calls.Should().Equal("expiry");
        tasks.BlockExpiry.SetResult(); await first;
        tasks.Calls.Should().Equal("expiry", "pending", "retry", "integrity", "observation");
    }

    [Test]
    public async Task NewRunnerAfterLockRelease_ShouldRespectSharedDueTimesAndFailureBackoff()
    {
        var tasks = new Tasks { FailExpiry = true }; var clock = new Clock(); var gate = new Gate();
        using var provider = new ServiceCollection().AddSingleton<IObjectAssetMaintenanceSchedule, MemorySchedule>()
            .AddScoped<IObjectAssetMaintenanceTasks>(_ => tasks).BuildServiceProvider();
        ObjectAssetMaintenanceRunner Create() => new(provider.GetRequiredService<IServiceScopeFactory>(), gate, new(), NullLogger<ObjectAssetMaintenanceRunner>.Instance, clock);
        await Create().RunOnceAsync();
        clock.Now = clock.Now.AddSeconds(10); await Create().RunOnceAsync();
        tasks.Calls.Should().HaveCount(5, "a fresh pod must observe the persisted failure and successful due times");
        tasks.FailExpiry = false; clock.Now = clock.Now.AddMinutes(2);
        await Create().RunOnceAsync(); tasks.Calls.Skip(5).Should().Equal("expiry");
        clock.Now = clock.Now.AddMinutes(14); await Create().RunOnceAsync();
        tasks.Calls.Skip(6).Should().Equal("pending", "retry", "integrity", "observation");
    }

    [Test]
    public async Task SharedSchedule_ShouldHonorIndependentEvidenceAndObservationIntervals()
    {
        var tasks = new Tasks(); var clock = new Clock(); var options = new ObjectStorageAssetRuntimeOptions();
        options.Maintenance.IntegrityEvidenceInterval = TimeSpan.FromMinutes(1);
        using var provider = new ServiceCollection().AddSingleton<IObjectAssetMaintenanceSchedule, MemorySchedule>()
            .AddScoped<IObjectAssetMaintenanceTasks>(_ => tasks).BuildServiceProvider();
        ObjectAssetMaintenanceRunner Create() => new(provider.GetRequiredService<IServiceScopeFactory>(), new Gate(), options, NullLogger<ObjectAssetMaintenanceRunner>.Instance, clock);
        await Create().RunOnceAsync(); clock.Now = clock.Now.AddMinutes(2); await Create().RunOnceAsync();
        tasks.Calls.Skip(5).Should().Equal("integrity");
    }

    private sealed class MemorySchedule : IObjectAssetMaintenanceSchedule
    {
        private readonly Dictionary<string, ObjectAssetMaintenanceState> _states = [];
        public Task<Dictionary<string, ObjectAssetMaintenanceState>> LoadAsync(string[] names, CancellationToken ct)
            => Task.FromResult(_states.ToDictionary(p => p.Key, p => p.Value));
        public Task SaveAsync(ObjectAssetMaintenanceState state, CancellationToken ct)
        { _states[state.Name] = state; return Task.CompletedTask; }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Tasks : IObjectAssetMaintenanceTasks
    {
        public List<string> Calls { get; } = [];
        public bool FailExpiry { get; set; }
        public TaskCompletionSource? BlockExpiry { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<int> ExpireCoreAsync(DateTimeOffset? utcNow, CancellationToken cancellationToken)
        {
            Calls.Add("expiry"); Started.TrySetResult();
            if (FailExpiry) throw new IOException("Injected failure");
            if (BlockExpiry is not null) await BlockExpiry.Task.WaitAsync(cancellationToken);
            return 1;
        }
        public Task<int> PendingCoreAsync(CancellationToken cancellationToken) { Calls.Add("pending"); return Task.FromResult(1); }
        public Task<int> RetryCoreAsync(CancellationToken cancellationToken, DateTimeOffset? failedBefore = null) { Calls.Add("retry"); return Task.FromResult(1); }
        public Task<int> SynchronizeIntegrityEvidenceAsync(CancellationToken cancellationToken) { Calls.Add("integrity"); return Task.FromResult(1); }
        public Task<IntegrityBacklogSnapshot> ObserveIntegrityEvidenceAsync(CancellationToken cancellationToken)
        { Calls.Add("observation"); return Task.FromResult(new IntegrityBacklogSnapshot(DateTimeOffset.UtcNow, 0, null, 0)); }
    }
    private sealed class Gate : IObjectAssetMaintenanceLock
    {
        private readonly SemaphoreSlim _gate = new(1);
        public Task<IObjectAssetMaintenanceLease?> TryAcquireAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IObjectAssetMaintenanceLease?>(_gate.Wait(0) ? new Lease(_gate, cancellationToken) : null);
        private sealed class Lease(SemaphoreSlim gate, CancellationToken token) : IObjectAssetMaintenanceLease
        {
            public CancellationToken CancellationToken => token;
            public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; }
        }
    }
}
