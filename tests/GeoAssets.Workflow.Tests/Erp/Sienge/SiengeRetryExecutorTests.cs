using FluentAssertions;
using GeoAssets.Workflow.Erp.Sienge;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Sienge;

public class SiengeRetryExecutorTests
{
    private static SiengeSyncRetryOptions Options(int maxAttempts = 3) => new()
    {
        BaseBackoffInterval = TimeSpan.FromSeconds(1),
        MaxBackoffInterval = TimeSpan.FromSeconds(10),
        MaxAttempts = maxAttempts,
    };

    [Fact(Timeout = 10_000)]
    public async Task ExecuteWithBackoffAsync_SucceedsFirstTry_NeverRetries()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        var attempts = 0;

        var result = await RunWithAutoAdvance(timeProvider, () =>
        {
            attempts++;
            return Task.FromResult(42);
        }, Options());

        result.Should().Be(42);
        attempts.Should().Be(1);
    }

    [Fact(Timeout = 10_000)]
    public async Task ExecuteWithBackoffAsync_FailsThenSucceeds_RetriesWithBackoffAndReturnsResult()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        var attempts = 0;

        var result = await RunWithAutoAdvance(timeProvider, () =>
        {
            attempts++;
            if (attempts < 3) throw new InvalidOperationException("transient");
            return Task.FromResult(99);
        }, Options(maxAttempts: 5));

        result.Should().Be(99);
        attempts.Should().Be(3);
    }

    [Fact(Timeout = 10_000)]
    public async Task ExecuteWithBackoffAsync_AlwaysFails_RethrowsAfterMaxAttemptsWithoutExceedingIt()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        var attempts = 0;

        Task<int> Run() => RunWithAutoAdvance<int>(timeProvider, () =>
        {
            attempts++;
            throw new InvalidOperationException("always fails");
        }, Options(maxAttempts: 3));

        var act = Run;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("always fails");
        attempts.Should().Be(3);
    }

    [Fact(Timeout = 10_000)]
    public async Task ExecuteWithBackoffAsync_BackoffDoublesEachAttemptUpToCap()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        var delaysObserved = new List<TimeSpan>();
        var attempts = 0;
        var lastCallAt = timeProvider.GetUtcNow();
        var options = new SiengeSyncRetryOptions { BaseBackoffInterval = TimeSpan.FromSeconds(1), MaxBackoffInterval = TimeSpan.FromSeconds(3), MaxAttempts = 5 };

        var result = await RunWithAutoAdvance(timeProvider, () =>
        {
            attempts++;
            var now = timeProvider.GetUtcNow();
            if (attempts > 1) delaysObserved.Add(now - lastCallAt);
            lastCallAt = now;
            if (attempts < 4) throw new InvalidOperationException("transient");
            return Task.FromResult(1);
        }, options);

        result.Should().Be(1);
        delaysObserved.Should().HaveCount(3);
        delaysObserved[0].Should().Be(TimeSpan.FromSeconds(1));
        delaysObserved[1].Should().Be(TimeSpan.FromSeconds(2));
        delaysObserved[2].Should().Be(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Runs <paramref name="operation"/> through <see cref="SiengeRetryExecutor.ExecuteWithBackoffAsync{T}"/>
    /// while a background loop keeps nudging <paramref name="timeProvider"/> forward in small
    /// fake-time increments (with a tiny real-time pause between nudges, so the executor's
    /// <c>Task.Delay</c> continuations get a chance to actually run) until the executor's task
    /// completes. <see cref="Task.WaitAsync(TimeSpan)"/> bounds the whole thing by real wall-clock
    /// time, so a logic bug that never completes fails the test instead of hanging the test run.
    /// </summary>
    private static async Task<T> RunWithAutoAdvance<T>(FakeTimeProvider timeProvider, Func<Task<T>> operation, SiengeSyncRetryOptions options)
    {
        var task = SiengeRetryExecutor.ExecuteWithBackoffAsync(_ => operation(), options, timeProvider);
        using var cts = new CancellationTokenSource();

        var advancer = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                timeProvider.Advance(TimeSpan.FromMilliseconds(100));
                await Task.Delay(5, CancellationToken.None).ConfigureAwait(false);
            }
        }, CancellationToken.None);

        try
        {
            return await task.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            await advancer.ConfigureAwait(false);
        }
    }
}
