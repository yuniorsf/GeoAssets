using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.NeutralOperator.Server.Tests.Registration;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that knows whether a timer is waiting to fire, so a test advances the clock only once the
/// registration loop has scheduled its next delay. The loop publishes its new state (Registered or Failing) before it calls
/// <c>Task.Delay</c>; advancing inside that window moved the clock before the timer existed, so the timer was then due after the
/// advance and never fired (a timeout under load).
/// </summary>
internal sealed class SchedulingAwareTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
{
    private int _pendingTimers;

    /// <summary>Timers created and not yet fired. The loop's delays are one-shot, so this is 1 while it waits and 0 while it works.</summary>
    public int PendingTimers => Volatile.Read(ref _pendingTimers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Interlocked.Increment(ref _pendingTimers);
        return base.CreateTimer(
            s =>
            {
                Interlocked.Decrement(ref _pendingTimers);
                callback(s);
            },
            state,
            dueTime,
            period);
    }

    /// <summary>Waits (in real time) until a timer is pending, then advances the clock by <paramref name="delta"/>.</summary>
    public async Task AdvanceWhenScheduledAsync(TimeSpan delta)
    {
        await RegistrationLoop.UntilAsync(() => PendingTimers > 0);
        Advance(delta);
    }
}
