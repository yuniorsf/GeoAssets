using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// XD01-400: <see cref="MunicipioGrantSyncPruner"/> over a fake outbox store and a <see cref="FakeTimeProvider"/> — batching,
/// retention cutoffs, the pruned counter and the background loop — plus one run over the real store on SQLite.
/// </summary>
public sealed class MunicipioGrantSyncPrunerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore : IMunicipioGrantSyncStore
    {
        private int _calls;

        public Queue<int> Results { get; } = new();

        public Exception? Failure { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public List<(DateTimeOffset CompletedBefore, DateTimeOffset? FailedBefore, int BatchSize)> Arguments { get; } = [];

        public Task<int> PruneAsync(DateTimeOffset completedBefore, DateTimeOffset? failedBefore, int batchSize, CancellationToken ct = default)
        {
            lock (Arguments) Arguments.Add((completedBefore, failedBefore, batchSize));
            Interlocked.Increment(ref _calls);
            if (Failure is not null) return Task.FromException<int>(Failure);
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : 0);
        }

        public Task<IReadOnlyList<MunicipioGrantSyncWorkItem>> ClaimDueAsync(int batchSize, TimeSpan leaseDuration, string workerId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkCompletedAsync(long id, string workerId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> ScheduleRetryAsync(long id, string workerId, string failure, DateTimeOffset nextAttemptAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkFailedAsync(long id, string workerId, string failure, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;

        public Fixture(MunicipioGrantSyncOptions? options = null)
        {
            Options = options ?? new MunicipioGrantSyncOptions();
            var services = new ServiceCollection();
            services.AddMetrics();
            services.AddScoped<IMunicipioGrantSyncStore>(_ => Store);
            _services = services.BuildServiceProvider();
            Pruner = new MunicipioGrantSyncPruner(
                _services.GetRequiredService<IServiceScopeFactory>(),
                Microsoft.Extensions.Options.Options.Create(Options),
                Time,
                new MunicipioGrantSyncMetrics(_services.GetRequiredService<IMeterFactory>()),
                NullLogger<MunicipioGrantSyncPruner>.Instance);
        }

        public MunicipioGrantSyncOptions Options { get; }

        public FakeStore Store { get; } = new();

        public FakeTimeProvider Time { get; } = new(Start);

        public MunicipioGrantSyncPruner Pruner { get; }

        public void Dispose()
        {
            Pruner.Dispose();
            _services.Dispose();
        }
    }

    private static MeterListener ListenToPruned(List<long> counts)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Name, MunicipioGrantSyncMetrics.PrunedCounterName, StringComparison.Ordinal)) l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            lock (counts) counts.Add(value);
        });
        listener.Start();
        return listener;
    }

    /// <summary>Moves the clock one interval on; repeated by the waits, since the loop may not have armed its delay yet.</summary>
    private static bool AdvanceAnHour(Fixture fixture)
    {
        fixture.Time.Advance(TimeSpan.FromHours(1));
        return true;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1000 && !condition(); attempt++) await Task.Delay(10);

        condition().Should().BeTrue("the condition should be met within 10 s");
    }

    [Fact]
    public async Task PruneOnce_LoopsUntilAShortBatch_AndSumsWhatItDeleted()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { PruneBatchSize = 500 });
        foreach (var result in new[] { 500, 500, 120 }) fixture.Store.Results.Enqueue(result);

        var total = await fixture.Pruner.PruneOnceAsync(CancellationToken.None);

        total.Should().Be(1120);
        fixture.Store.Calls.Should().Be(3);
        fixture.Store.Arguments.Should().OnlyContain(a => a.BatchSize == 500);
    }

    [Fact]
    public async Task PruneOnce_AfterAFullLastBatch_AsksOnceMore_AndStopsOnTheEmptyOne()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { PruneBatchSize = 4 });
        fixture.Store.Results.Enqueue(4);
        fixture.Store.Results.Enqueue(0);

        var total = await fixture.Pruner.PruneOnceAsync(CancellationToken.None);

        total.Should().Be(4);
        fixture.Store.Calls.Should().Be(2);
    }

    [Fact]
    public async Task PruneOnce_WithNothingToDelete_AsksOnce_AndReturnsZero()
    {
        using var fixture = new Fixture();

        (await fixture.Pruner.PruneOnceAsync(CancellationToken.None)).Should().Be(0);

        fixture.Store.Calls.Should().Be(1);
    }

    [Fact]
    public async Task PruneOnce_UsesTheClockMinusEachRetention()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions
        {
            CompletedRetention = TimeSpan.FromDays(10),
            FailedRetention = TimeSpan.FromDays(60),
        });

        await fixture.Pruner.PruneOnceAsync(CancellationToken.None);
        fixture.Time.Advance(TimeSpan.FromDays(1));
        await fixture.Pruner.PruneOnceAsync(CancellationToken.None);

        fixture.Store.Arguments.Should().Equal(
            (Start - TimeSpan.FromDays(10), (DateTimeOffset?)(Start - TimeSpan.FromDays(60)), 500),
            (Start + TimeSpan.FromDays(1) - TimeSpan.FromDays(10), (DateTimeOffset?)(Start + TimeSpan.FromDays(1) - TimeSpan.FromDays(60)), 500));
    }

    [Fact]
    public async Task PruneOnce_WithoutAFailedRetention_PassesNoFailedCutoff()
    {
        using var fixture = new Fixture();

        await fixture.Pruner.PruneOnceAsync(CancellationToken.None);

        var call = fixture.Store.Arguments.Should().ContainSingle().Subject;
        call.FailedBefore.Should().BeNull();
        call.CompletedBefore.Should().Be(Start - TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task PruneOnce_CountsWhatItDeleted_OnTheCounter_AndNothingWhenZero()
    {
        var counts = new List<long>();
        using var listener = ListenToPruned(counts);
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { PruneBatchSize = 10 });
        fixture.Store.Results.Enqueue(10);
        fixture.Store.Results.Enqueue(3);

        await fixture.Pruner.PruneOnceAsync(CancellationToken.None);
        await fixture.Pruner.PruneOnceAsync(CancellationToken.None);

        counts.Should().Equal(13L);
    }

    [Fact]
    public async Task PruneOnce_ForwardsCancellation()
    {
        using var fixture = new Fixture();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => fixture.Pruner.PruneOnceAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Store.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Loop_RunsSoonAfterStart_ThenEveryInterval()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { PruneInterval = TimeSpan.FromHours(1) });
        await fixture.Pruner.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => fixture.Store.Calls >= 1);
            await Task.Delay(100);
            fixture.Store.Calls.Should().Be(1, "the next run is not due until the interval has passed");

            await WaitUntilAsync(() => AdvanceAnHour(fixture) && fixture.Store.Calls >= 2);
        }
        finally
        {
            await fixture.Pruner.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Loop_AThrowingStore_DoesNotEscape_AndTheNextIntervalTriesAgain()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { PruneInterval = TimeSpan.FromHours(1) });
        fixture.Store.Failure = new InvalidOperationException("database down");
        await fixture.Pruner.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => fixture.Store.Calls >= 1);
            await WaitUntilAsync(() => AdvanceAnHour(fixture) && fixture.Store.Calls >= 2);

            fixture.Pruner.ExecuteTask.Should().NotBeNull();
            fixture.Pruner.ExecuteTask!.IsFaulted.Should().BeFalse();
            fixture.Pruner.ExecuteTask.IsCompleted.Should().BeFalse("the loop is still running");
        }
        finally
        {
            await fixture.Pruner.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Loop_WhenDisabled_NeverPrunes()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { Enabled = false });
        await fixture.Pruner.StartAsync(CancellationToken.None);
        try
        {
            fixture.Time.Advance(TimeSpan.FromDays(2));
            await Task.Delay(100);

            fixture.Store.Calls.Should().Be(0);
        }
        finally
        {
            await fixture.Pruner.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Host_RegistersThePruner_AndItPrunesTheRealStore_KeepingEveryStatus()
    {
        await using var host = await MunicipioGrantSyncHost.StartAsync(new MunicipioGrantSyncOptions { CompletedRetention = TimeSpan.FromDays(30) });
        var pruner = host.Host.Services.GetServices<IHostedService>().OfType<MunicipioGrantSyncPruner>().Single();
        var (_, id, _) = await host.CreateAsync(MunicipioGrantSyncHost.PrefeituraA);
        await host.DrainAsync();
        (await host.UpdateAsync(id, MunicipioGrantSyncHost.PrefeituraB)).Should().Be(System.Net.HttpStatusCode.NoContent);
        await host.DrainAsync();
        (await host.RequestCountAsync()).Should().Be(2);
        var before = await host.StatusAsync();

        (await pruner.PruneOnceAsync(CancellationToken.None)).Should().Be(0, "nothing is older than the retention yet");

        host.Time.Advance(TimeSpan.FromDays(31));
        (await pruner.PruneOnceAsync(CancellationToken.None)).Should().Be(1, "the older request is past the retention, the latest is kept");

        (await host.RequestCountAsync()).Should().Be(1);
        (await host.StatusAsync()).Should().Be(before);
        (await pruner.PruneOnceAsync(CancellationToken.None)).Should().Be(0);
    }
}
