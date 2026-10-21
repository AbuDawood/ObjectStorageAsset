using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elf.ObjectStorageAsset.UnitTests;

[TestFixture]
public sealed class MaintenanceRunnerTests
{
    [Test]
    public async Task FailedTask_ShouldNotBlockSubsequentTasksAndShouldRecoverNextCycle()
    {
        var tasks = new Tasks { FailExpiry = true }; var clock = new Clock();
        var resolutions = 0;
        using var provider = new ServiceCollection().AddScoped<IObjectAssetMaintenanceTasks>(_ => { resolutions++; return tasks; }).BuildServiceProvider();
        var runner = new ObjectAssetMaintenanceRunner(provider.GetRequiredService<IServiceScopeFactory>(), new Gate(),
            new(), NullLogger<ObjectAssetMaintenanceRunner>.Instance, clock);
        await runner.RunOnceAsync();
        tasks.Calls.Should().Equal("expiry", "pending", "retry");
        resolutions.Should().Be(3, "each task must receive a fresh scope");
        tasks.FailExpiry = false; clock.Now = clock.Now.AddMinutes(16);
        await runner.RunOnceAsync();
        tasks.Calls.Should().Equal("expiry", "pending", "retry", "expiry", "pending", "retry");
        resolutions.Should().Be(6);
    }

    [Test]
    public async Task ConcurrentRunners_ShouldSkipWhenOneHoldsLock()
    {
        var tasks = new Tasks { BlockExpiry = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var provider = new ServiceCollection().AddScoped<IObjectAssetMaintenanceTasks>(_ => tasks).BuildServiceProvider();
        var guard = new Gate(); var clock = new Clock();
        ObjectAssetMaintenanceRunner Create() => new(provider.GetRequiredService<IServiceScopeFactory>(), guard, new(), NullLogger<ObjectAssetMaintenanceRunner>.Instance, clock);
        var first = Create().RunOnceAsync();
        await tasks.Started.Task;
        await Create().RunOnceAsync();
        tasks.Calls.Should().Equal("expiry");
        tasks.BlockExpiry.SetResult(); await first;
        tasks.Calls.Should().Equal("expiry", "pending", "retry");
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
