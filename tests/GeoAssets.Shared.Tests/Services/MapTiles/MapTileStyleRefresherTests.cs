using FluentAssertions;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.MapTiles;
using GeoAssets.Shared.Tests.Services.OrderTargets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.MapTiles;

/// <summary>XD01-435: when a MapLibre map re-reads the tile style — periodically, after this client's writes
/// (debounced), one refresh per map at a time, and never once detached or disposed.</summary>
public sealed class MapTileStyleRefresherTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WriteDelay = TimeSpan.FromSeconds(1.5);

    private sealed class Recorder
    {
        public List<string> Refreshed { get; } = [];
        public Func<string, Task>? Behaviour { get; set; }

        public Task RefreshAsync(string divId, CancellationToken cancellationToken)
        {
            Refreshed.Add(divId);
            return Behaviour?.Invoke(divId) ?? Task.CompletedTask;
        }
    }

    private static MapTileStyleRefresher Sut(Recorder recorder, FakeTimeProvider time, TimeSpan? interval = null, CountingLogger<MapTileStyleRefresher>? logger = null) =>
        new(recorder.RefreshAsync,
            new MapLibreOptions { StyleRefreshInterval = interval ?? Interval, WriteRefreshDelay = WriteDelay },
            time, (Microsoft.Extensions.Logging.ILogger?)logger ?? NullLogger.Instance);

    [Fact]
    public void An_attached_map_is_refreshed_every_interval()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder();
        using var sut = Sut(recorder, time);

        sut.Attach("map1");
        time.Advance(Interval - TimeSpan.FromSeconds(1));
        recorder.Refreshed.Should().BeEmpty();

        time.Advance(TimeSpan.FromSeconds(1));
        time.Advance(Interval);

        recorder.Refreshed.Should().Equal("map1", "map1");
        sut.AttachedMaps.Should().Equal("map1");
    }

    [Fact]
    public void A_zero_interval_refreshes_only_after_writes()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder();
        using var sut = Sut(recorder, time, TimeSpan.Zero);
        sut.Attach("map1");

        time.Advance(TimeSpan.FromHours(1));
        recorder.Refreshed.Should().BeEmpty();

        sut.NotifyWrite();
        time.Advance(WriteDelay);
        recorder.Refreshed.Should().Equal("map1");
    }

    [Fact]
    public void Writes_within_the_delay_are_one_refresh_after_the_last_of_them()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder();
        using var sut = Sut(recorder, time);
        sut.Attach("map1");

        sut.NotifyWrite();
        time.Advance(TimeSpan.FromSeconds(1));
        sut.NotifyWrite();
        time.Advance(TimeSpan.FromSeconds(1));
        recorder.Refreshed.Should().BeEmpty("the second write restarted the delay");

        time.Advance(TimeSpan.FromSeconds(0.5));
        recorder.Refreshed.Should().Equal("map1");

        time.Advance(WriteDelay * 4);
        recorder.Refreshed.Should().Equal("map1");

        sut.NotifyWrite();
        time.Advance(WriteDelay);
        recorder.Refreshed.Should().Equal("map1", "map1");
    }

    [Fact]
    public void A_write_refreshes_every_attached_map_and_none_detached()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder();
        using var sut = Sut(recorder, time);
        sut.Attach("map1");
        sut.Attach("map2");
        sut.Attach("map2");
        sut.Detach("map1");
        sut.Detach("unknown");

        sut.NotifyWrite();
        time.Advance(WriteDelay);
        time.Advance(Interval);

        recorder.Refreshed.Should().Equal("map2", "map2");
        sut.AttachedMaps.Should().Equal("map2");
    }

    [Fact]
    public void A_write_before_any_map_is_attached_refreshes_nothing()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder();
        using var sut = Sut(recorder, time);

        sut.NotifyWrite();
        sut.Attach("map1");
        time.Advance(WriteDelay);

        recorder.Refreshed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refresh_asked_for_while_one_runs_runs_once_right_after_it()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder();
        var gate = new TaskCompletionSource();
        recorder.Behaviour = _ => recorder.Refreshed.Count == 1 ? gate.Task : Task.CompletedTask;
        using var sut = Sut(recorder, time);
        sut.Attach("map1");

        time.Advance(Interval);
        sut.NotifyWrite();
        time.Advance(WriteDelay);
        sut.NotifyWrite();
        time.Advance(WriteDelay);
        recorder.Refreshed.Should().Equal(["map1"], "the first refresh is still running");

        gate.SetResult();
        await Task.Yield();

        recorder.Refreshed.Should().Equal("map1", "map1");
    }

    [Fact]
    public void A_failing_refresh_is_logged_and_the_next_one_still_runs()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder { Behaviour = _ => throw new InvalidOperationException("JS disconnected") };
        var logger = new CountingLogger<MapTileStyleRefresher>();
        using var sut = Sut(recorder, time, logger: logger);
        sut.Attach("map1");

        time.Advance(Interval);
        time.Advance(Interval);

        recorder.Refreshed.Should().Equal("map1", "map1");
        logger.Warnings.Should().Be(2);
    }

    [Fact]
    public void A_faulted_refresh_task_is_logged_too()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder { Behaviour = _ => Task.FromException(new TimeoutException()) };
        var logger = new CountingLogger<MapTileStyleRefresher>();
        using var sut = Sut(recorder, time, logger: logger);
        sut.Attach("map1");

        time.Advance(Interval);

        logger.Warnings.Should().Be(1);
    }

    [Fact]
    public void Nothing_is_refreshed_once_disposed()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder();
        var sut = Sut(recorder, time);
        sut.Attach("map1");
        sut.NotifyWrite();

        sut.Dispose();
        sut.Dispose();
        sut.Attach("map2");
        time.Advance(Interval * 3);

        recorder.Refreshed.Should().BeEmpty();
        sut.AttachedMaps.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refresh_cancelled_by_dispose_is_not_logged()
    {
        var time = new FakeTimeProvider();
        var recorder = new Recorder();
        var logger = new CountingLogger<MapTileStyleRefresher>();
        var started = new TaskCompletionSource<CancellationToken>();
        var sut = new MapTileStyleRefresher(
            async (_, ct) => { started.SetResult(ct); await Task.Delay(Timeout.Infinite, ct); },
            new MapLibreOptions { StyleRefreshInterval = Interval }, time, logger);
        sut.Attach("map1");
        time.Advance(Interval);
        var token = await started.Task;

        sut.Dispose();
        await Task.Yield();

        token.IsCancellationRequested.Should().BeTrue();
        logger.Warnings.Should().Be(0);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Negative_intervals_are_refused(int intervalSeconds, int delaySeconds)
    {
        var act = () => new MapTileStyleRefresher(
            (_, _) => Task.CompletedTask,
            new MapLibreOptions { StyleRefreshInterval = TimeSpan.FromSeconds(intervalSeconds), WriteRefreshDelay = TimeSpan.FromSeconds(delaySeconds) },
            new FakeTimeProvider(), NullLogger.Instance);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Attach_refuses_a_blank_div_id()
    {
        using var sut = Sut(new Recorder(), new FakeTimeProvider());

        var act = () => sut.Attach(" ");

        act.Should().Throw<ArgumentException>();
    }
}
