using FluentAssertions;
using GeoAssets.Web.Services.Session;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Web.Tests.Session;

public class SessionTimeoutServiceTests
{
    private static IOptions<SessionConfig> Config(int timeoutMinutes, int warningSeconds)
        => Options.Create(new SessionConfig
        {
            InactivityTimeoutMinutes    = timeoutMinutes,
            WarningBeforeTimeoutSeconds = warningSeconds
        });

    /// <summary>
    /// <see cref="SessionTimeoutService.Start"/> synchronously constructs its internal
    /// <see cref="PeriodicTimer"/> before suspending on the first tick, so its timer is
    /// guaranteed registered by the time <c>Start()</c> returns — unlike AssetService's
    /// Task.Run-based debounce, no pre-Advance settle wait is needed. A short settle wait
    /// *after* Advance() is still needed for the tick's continuation (event firing, field
    /// updates) to run on the thread pool; this is scheduler settling, not simulated time —
    /// the 1-minute/10-second thresholds under test are driven entirely by the fake clock.
    /// </summary>
    private static Task SettleSchedulerAsync() => Task.Delay(50);

    [Fact]
    public async Task RunAsync_BelowWarningThreshold_DoesNotEnterWarning()
    {
        var timeProvider = new FakeTimeProvider();
        await using var sut = new SessionTimeoutService(Config(timeoutMinutes: 1, warningSeconds: 10), timeProvider);

        sut.Start();
        timeProvider.Advance(TimeSpan.FromSeconds(30)); // 60 - 30 = 30s remaining, above the 10s warning threshold
        await SettleSchedulerAsync();

        sut.IsInWarning.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WithinWarningWindow_FiresOnStateChangedAndSetsIsInWarning()
    {
        var timeProvider = new FakeTimeProvider();
        await using var sut = new SessionTimeoutService(Config(timeoutMinutes: 1, warningSeconds: 10), timeProvider);
        var fired = false;
        sut.OnStateChanged += () => fired = true;

        sut.Start();
        timeProvider.Advance(TimeSpan.FromSeconds(51)); // 60 - 51 = 9s remaining, at/below the 10s warning threshold
        await SettleSchedulerAsync();

        sut.IsInWarning.Should().BeTrue();
        fired.Should().BeTrue();
        sut.SecondsLeft.Should().Be(9);
    }

    [Fact]
    public async Task RunAsync_TimeoutReached_FiresOnTimeoutAndClearsWarning()
    {
        var timeProvider = new FakeTimeProvider();
        await using var sut = new SessionTimeoutService(Config(timeoutMinutes: 1, warningSeconds: 10), timeProvider);
        var timedOut = false;
        sut.OnTimeout += () => timedOut = true;

        sut.Start();
        timeProvider.Advance(TimeSpan.FromSeconds(60));
        await SettleSchedulerAsync();

        timedOut.Should().BeTrue();
        sut.IsInWarning.Should().BeFalse();
        sut.SecondsLeft.Should().Be(0);
    }

    [Fact]
    public async Task RecordActivity_DuringWarning_DismissesOverlayAndResetsCountdown()
    {
        var timeProvider = new FakeTimeProvider();
        await using var sut = new SessionTimeoutService(Config(timeoutMinutes: 1, warningSeconds: 10), timeProvider);

        sut.Start();
        timeProvider.Advance(TimeSpan.FromSeconds(51));
        await SettleSchedulerAsync();
        sut.IsInWarning.Should().BeTrue();

        sut.RecordActivity();

        sut.IsInWarning.Should().BeFalse();
        sut.SecondsLeft.Should().Be(60);
    }
}
