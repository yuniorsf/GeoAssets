using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Modules.Occupancy;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <see cref="OccupancyApplySagaSweeper"/> (XD01-383): a saga still pending after <see cref="OccupancyApplySagaOptions.ApplyTimeout"/>
/// is flagged, counted once and warned about — never reverted — and the backlog gauges say how many wait and for how long.
/// A fake clock decides when a saga is stale; the in-memory monitor is the TestKit's.
/// </summary>
public sealed class OccupancyApplySagaSweeperTests : IDisposable
{
    private static readonly Guid Organization = new("0e0e0e0e-0000-4000-8000-000000000383");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero));
    private readonly string _meterVersion = $"sweeper-{Guid.NewGuid():N}";
    private readonly GeoAssetsActivitySource _tracer = new("1.0.0");
    private readonly GeoAssetsMeter _metrics;
    private readonly MeterListener _meterListener = new();
    private readonly List<(string Instrument, double Value)> _measurements = [];
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _activityListener;
    private readonly CapturingLoggerProvider _logs = new();
    private readonly InMemoryOccupancyApplySagaStore _store = new();

    public OccupancyApplySagaSweeperTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, GeoAssetsActivitySource.SourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (_activities) _activities.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, GeoAssetsMeter.MeterName, StringComparison.Ordinal)
                && string.Equals(instrument.Meter.Version, _meterVersion, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) => Record(instrument.Name, value));
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, _, _) => Record(instrument.Name, value));
        _meterListener.Start();
        _metrics = new GeoAssetsMeter(_meterVersion);
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
        _metrics.Dispose();
        _tracer.Dispose();
        _logs.Dispose();
    }

    private void Record(string instrument, double value)
    {
        lock (_measurements) _measurements.Add((instrument, value));
    }

    private double Total(string instrument)
    {
        lock (_measurements) return _measurements.Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal)).Sum(m => m.Value);
    }

    /// <summary>The gauge's current reading: the observable instruments are pulled on demand.</summary>
    private double Gauge(string instrument)
    {
        lock (_measurements) _measurements.Clear();
        _meterListener.RecordObservableInstruments();
        lock (_measurements) return _measurements.Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal)).Sum(m => m.Value);
    }

    private OccupancyApplySagaSweeper Sweeper(
        OccupancyApplySagaOptions? options = null, IOccupancyApplySagaMonitor? monitor = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => monitor ?? _store);
        var provider = services.BuildServiceProvider();
        return new OccupancyApplySagaSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new OccupancyApplySagaOptions { ApplyTimeout = TimeSpan.FromMinutes(15) }),
            _clock, _tracer, _metrics, _logs.CreateLogger<OccupancyApplySagaSweeper>());
    }

    private async Task<OccupancyApplySaga> PendingAsync(string orderId, TimeSpan age = default)
    {
        var requested = AssetMutationBatchRequestedEvent.Create(
            "default", Organization, "tests", "approver-1", [], _clock, batchId: OccupancyCommitService.AssetMutationBatchId(orderId),
            sourceOrderId: orderId, sourceVerticalId: OccupancyApplySagaService.VerticalId);
        var saga = new OccupancyApplySaga
        {
            BatchId = requested.BatchId,
            Attempt = 1,
            OrderId = orderId,
            OrderTypeId = "pole-work",
            OrganizationId = Organization,
            PreviousStatus = "Completed",
            ApprovedStatus = "Approved",
            RequestedBy = "approver-1",
            RequestedAt = _clock.GetUtcNow() - age,
            LastRequestedAt = _clock.GetUtcNow() - age,
            State = OccupancyApplySagaState.Pending,
            Batch = requested,
        };
        (await _store.BeginAsync(saga, [], [], CancellationToken.None)).Should().BeTrue();
        return saga;
    }

    [Fact]
    public async Task SweepOnceAsync_BeforeTheTimeout_FlagsNothing_CountsNothing_AndReportsTheBacklog()
    {
        var young = await PendingAsync("ord-young", TimeSpan.FromMinutes(14));
        var sut = Sweeper();

        var flagged = await sut.SweepOnceAsync(CancellationToken.None);

        flagged.Should().Be(0);
        (await _store.FindAsync(young.BatchId, CancellationToken.None))!.TimedOutAt.Should().BeNull();
        Total("geoassets.saga.apply.timeouts").Should().Be(0);
        _logs.Entries.Should().BeEmpty();
        Gauge("geoassets.saga.apply.pending").Should().Be(1);
        Gauge("geoassets.saga.apply.oldest_pending_age").Should().Be(TimeSpan.FromMinutes(14).TotalSeconds);
    }

    [Fact]
    public async Task SweepOnceAsync_AfterTheTimeout_FlagsTheSaga_KeepsItPending_CountsItOnce_AndWarns()
    {
        var saga = await PendingAsync("ord-stale", TimeSpan.FromMinutes(16));
        var sut = Sweeper();

        var first = await sut.SweepOnceAsync(CancellationToken.None);
        var second = await sut.SweepOnceAsync(CancellationToken.None);

        first.Should().Be(1);
        second.Should().Be(0, "a flagged saga is not flagged again");
        var stored = (await _store.FindAsync(saga.BatchId, CancellationToken.None))!;
        stored.State.Should().Be(OccupancyApplySagaState.Pending, "a timeout never reverts: the kernel may still answer");
        stored.TimedOutAt.Should().Be(_clock.GetUtcNow());
        Total("geoassets.saga.apply.timeouts").Should().Be(1, "counted once, not once per sweep");
        var warning = _logs.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain("ord-stale").And.Contain(saga.BatchId.ToString()).And.Contain(Organization.ToString()).And.Contain("stays pending");
        Gauge("geoassets.saga.apply.pending").Should().Be(1, "it still waits");
        Gauge("geoassets.saga.apply.oldest_pending_age").Should().Be(TimeSpan.FromMinutes(16).TotalSeconds);
    }

    [Fact]
    public async Task SweepOnceAsync_WithNothingPending_ReportsAnEmptyBacklog()
    {
        var saga = await PendingAsync("ord-done", TimeSpan.FromMinutes(1));
        await _store.CompleteAsync(saga with { State = OccupancyApplySagaState.Committed, CompletedAt = _clock.GetUtcNow() }, CancellationToken.None);

        var flagged = await Sweeper().SweepOnceAsync(CancellationToken.None);

        flagged.Should().Be(0);
        Gauge("geoassets.saga.apply.pending").Should().Be(0);
        Gauge("geoassets.saga.apply.oldest_pending_age").Should().Be(0);
    }

    [Fact]
    public async Task SweepOnceAsync_OverMoreThanOnePage_FlagsEverySagaWithinTheSweepsPageLimit_AndTheRestOnTheNextSweep()
    {
        foreach (var index in Enumerable.Range(1, 5))
            await PendingAsync($"ord-{index}", TimeSpan.FromMinutes(20 + index));
        var sut = Sweeper(new OccupancyApplySagaOptions { ApplyTimeout = TimeSpan.FromMinutes(15), SweepBatchSize = 2, MaxPagesPerSweep = 2 });

        var first = await sut.SweepOnceAsync(CancellationToken.None);
        var second = await sut.SweepOnceAsync(CancellationToken.None);

        first.Should().Be(4, "two pages of two");
        second.Should().Be(1, "what the page limit left");
        _store.All.Should().OnlyContain(saga => saga.TimedOutAt != null && saga.State == OccupancyApplySagaState.Pending);
        Total("geoassets.saga.apply.timeouts").Should().Be(5);
        _logs.Entries.Should().HaveCount(5);
    }

    [Fact]
    public async Task SweepOnceAsync_FlagsTheOldestFirst()
    {
        var oldest = await PendingAsync("ord-oldest", TimeSpan.FromMinutes(90));
        var newer = await PendingAsync("ord-newer", TimeSpan.FromMinutes(20));
        var sut = Sweeper(new OccupancyApplySagaOptions { ApplyTimeout = TimeSpan.FromMinutes(15), SweepBatchSize = 1, MaxPagesPerSweep = 1 });

        await sut.SweepOnceAsync(CancellationToken.None);

        (await _store.FindAsync(oldest.BatchId, CancellationToken.None))!.TimedOutAt.Should().NotBeNull();
        (await _store.FindAsync(newer.BatchId, CancellationToken.None))!.TimedOutAt.Should().BeNull();
    }

    [Fact]
    public async Task SweepOnceAsync_StartsATimeoutSweepSpan_WithTheNumbersItFound()
    {
        await PendingAsync("ord-a", TimeSpan.FromMinutes(30));
        await PendingAsync("ord-b", TimeSpan.FromMinutes(1));

        await Sweeper().SweepOnceAsync(CancellationToken.None);

        Activity span;
        lock (_activities) span = _activities.Last(a => string.Equals(a.OperationName, "Saga.TimeoutSweep", StringComparison.Ordinal));
        span.GetTagItem("saga.timed_out").Should().Be(1);
        span.GetTagItem("saga.pending").Should().Be(2);
    }

    [Fact]
    public async Task SweepOnceAsync_WithACancelledToken_Throws_AndFlagsNothing()
    {
        await PendingAsync("ord-stale", TimeSpan.FromMinutes(30));
        var sut = Sweeper(monitor: new CancellationAwareMonitor(_store));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.SweepOnceAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.All.Single().TimedOutAt.Should().BeNull();
        Total("geoassets.saga.apply.timeouts").Should().Be(0);
    }

    [Fact]
    public async Task Service_SweepsOnStartAndEveryInterval_AndStopsWhenCancelled()
    {
        await PendingAsync("ord-stale", TimeSpan.FromMinutes(30));
        var monitor = new CountingMonitor(_store);
        using var sut = Sweeper(new OccupancyApplySagaOptions { ApplyTimeout = TimeSpan.FromMinutes(15), SweepInterval = TimeSpan.FromMinutes(1) }, monitor);

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => monitor.Sweeps == 1);
        await Task.Delay(50);
        monitor.Sweeps.Should().Be(1, "the first sweep runs when the service starts");
        _clock.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(50);
        monitor.Sweeps.Should().Be(1, "the interval has not elapsed");
        _clock.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => monitor.Sweeps == 2);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await WaitUntilAsync(() => monitor.Sweeps == 3);

        await sut.StopAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        monitor.Sweeps.Should().Be(3, "a stopped service sweeps no more");
        _logs.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Error, "a cancellation is a stop, not a failure");
    }

    [Fact]
    public async Task Service_WithTheSweeperDisabled_NeverSweeps()
    {
        var monitor = new CountingMonitor(_store);
        using var sut = Sweeper(new OccupancyApplySagaOptions { EnableSweeper = false }, monitor);

        await sut.StartAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(1));
        await sut.StopAsync(CancellationToken.None);

        monitor.Sweeps.Should().Be(0);
    }

    [Fact]
    public async Task Service_ASweepThatFails_IsLoggedAsAnError_AndTheNextOneRunsOnSchedule()
    {
        var monitor = new CountingMonitor(_store) { FailFirstWith = new TimeoutException("db down") };
        using var sut = Sweeper(new OccupancyApplySagaOptions { SweepInterval = TimeSpan.FromMinutes(1) }, monitor);

        await sut.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _logs.Entries.Count == 1);
        _logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await WaitUntilAsync(() => monitor.Sweeps == 2);
        await sut.StopAsync(CancellationToken.None);

        monitor.Sweeps.Should().Be(2);
    }

    [Fact]
    public void Constructor_NullCollaborator_ThrowsArgumentNullException()
    {
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new OccupancyApplySagaOptions());
        var logger = _logs.CreateLogger<OccupancyApplySagaSweeper>();

        ((Action)(() => _ = new OccupancyApplySagaSweeper(null!, options, _clock, _tracer, _metrics, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OccupancyApplySagaSweeper(scopes, null!, _clock, _tracer, _metrics, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OccupancyApplySagaSweeper(scopes, options, null!, _tracer, _metrics, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OccupancyApplySagaSweeper(scopes, options, _clock, null!, _metrics, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OccupancyApplySagaSweeper(scopes, options, _clock, _tracer, null!, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OccupancyApplySagaSweeper(scopes, options, _clock, _tracer, _metrics, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Options_DefaultToAFifteenMinuteTimeout_SweptEveryMinute()
    {
        var options = new OccupancyApplySagaOptions();

        options.ApplyTimeout.Should().Be(TimeSpan.FromMinutes(15));
        options.SweepInterval.Should().Be(TimeSpan.FromMinutes(1));
        options.EnableSweeper.Should().BeTrue();
        OccupancyApplySagaOptions.SectionName.Should().Be("Saga");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        condition().Should().BeTrue("the service should have swept by now");
    }

    private class CancellationAwareMonitor(IOccupancyApplySagaMonitor inner) : IOccupancyApplySagaMonitor
    {
        public virtual Task<IReadOnlyList<OccupancyApplySaga>> MarkTimedOutAsync(DateTimeOffset staleBefore, DateTimeOffset now, int max, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return inner.MarkTimedOutAsync(staleBefore, now, max, ct);
        }

        public Task<(int Pending, DateTimeOffset? OldestLastRequestedAt)> GetBacklogAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return inner.GetBacklogAsync(ct);
        }
    }

    private sealed class CountingMonitor(IOccupancyApplySagaMonitor inner) : CancellationAwareMonitor(inner)
    {
        private int _sweeps;

        public int Sweeps => Volatile.Read(ref _sweeps);

        public Exception? FailFirstWith { get; init; }

        public override Task<IReadOnlyList<OccupancyApplySaga>> MarkTimedOutAsync(DateTimeOffset staleBefore, DateTimeOffset now, int max, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _sweeps);
            if (n == 1 && FailFirstWith is not null)
                throw FailFirstWith;
            return base.MarkTimedOutAsync(staleBefore, now, max, ct);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries) return [.. _entries];
            }
        }

        public ILogger<T> CreateLogger<T>() => new Logger<T>(this);

        public ILogger CreateLogger(string categoryName) => new Logger<object>(this);

        public void Dispose()
        {
        }

        private sealed class Logger<T>(CapturingLoggerProvider owner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries) owner._entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
