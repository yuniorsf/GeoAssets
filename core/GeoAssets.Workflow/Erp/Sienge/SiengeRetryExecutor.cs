namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>
/// Retries <paramref name="operation"/> with exponential backoff (base interval doubling, capped
/// at <see cref="SiengeSyncRetryOptions.MaxBackoffInterval"/>) up to
/// <see cref="SiengeSyncRetryOptions.MaxAttempts"/> times. Rethrows the final failure's exception
/// unchanged after exhausting attempts — <see cref="SiengeNightlySyncOrchestrator"/> catches that
/// per operation and records it in the run result, so a failure is surfaced to the caller rather
/// than silently dropped, without one operation's failure aborting the other.
/// </summary>
public static class SiengeRetryExecutor
{
    public static async Task<T> ExecuteWithBackoffAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        SiengeSyncRetryOptions options,
        TimeProvider timeProvider,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return await operation(ct).ConfigureAwait(false);
            }
            catch (Exception) when (attempt < options.MaxAttempts)
            {
                var delay = TimeSpan.FromTicks(options.BaseBackoffInterval.Ticks * (1L << (attempt - 1)));
                if (delay > options.MaxBackoffInterval) delay = options.MaxBackoffInterval;
                await Task.Delay(delay, timeProvider, ct).ConfigureAwait(false);
            }
        }
    }
}
