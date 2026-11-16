namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>
/// Retries <paramref name="operation"/> with exponential backoff (base interval doubling, capped
/// at <see cref="TotvsSyncRetryOptions.MaxBackoffInterval"/>) up to
/// <see cref="TotvsSyncRetryOptions.MaxAttempts"/> times. Rethrows the final failure's exception
/// unchanged after exhausting attempts — <c>TotvsNightlySyncOrchestrator</c> catches that per
/// operation and records it in the run result, so a failure is surfaced to the caller rather
/// than silently dropped (XD01-253's "Sync failures are retried with backoff and surfaced, not
/// silently dropped" AC) without one operation's failure aborting the other two.
/// </summary>
public static class TotvsRetryExecutor
{
    public static async Task<T> ExecuteWithBackoffAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TotvsSyncRetryOptions options,
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
