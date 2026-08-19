using System.Diagnostics;
using GeoAssets.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services.Observability;

/// <summary>
/// Shared timing scaffold for the observable decorator chain.
/// Each protected Track* method creates an <see cref="Activity"/> span, runs the
/// wrapped operation, records elapsed time, and forwards exceptions with error status.
/// </summary>
public abstract class ObservableDecoratorBase<T>
{
    protected readonly ILogger<T> Logger;
    protected readonly TimeProvider TimeProvider;

    protected ObservableDecoratorBase(ILogger<T> logger, TimeProvider timeProvider)
    {
        Logger = logger;
        TimeProvider = timeProvider;
    }

    /// <summary>Tracks a <c>Task</c>-returning operation.</summary>
    /// <param name="spanName">Name of the child span to create.</param>
    /// <param name="operation">Delegate to execute.</param>
    /// <param name="before">Optional: called with the new span to set input tags.</param>
    /// <param name="after">Optional: called with (span, elapsedMs) on success — use for logging and metric recording.</param>
    protected async Task TrackAsync(
        string spanName,
        Func<Task> operation,
        Action<Activity?>? before = null,
        Action<Activity?, long>? after = null)
    {
        var startTimestamp = TimeProvider.GetTimestamp();
        using var span = ImportDiagnostics.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        before?.Invoke(span);
        try
        {
            await operation();
            var elapsedMs = (long)TimeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
            span?.SetTag("duration_ms", elapsedMs);
            after?.Invoke(span, elapsedMs);
        }
        catch (Exception ex)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.Message)
                 .AddException(ex);
            throw;
        }
    }

    /// <summary>Tracks a <c>Task&lt;TResult&gt;</c>-returning operation.</summary>
    /// <param name="after">Called with (span, result, elapsedMs) on success.</param>
    protected async Task<TResult> TrackAsync<TResult>(
        string spanName,
        Func<Task<TResult>> operation,
        Action<Activity?>? before = null,
        Action<Activity?, TResult, long>? after = null)
    {
        var startTimestamp = TimeProvider.GetTimestamp();
        using var span = ImportDiagnostics.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        before?.Invoke(span);
        try
        {
            var result = await operation();
            var elapsedMs = (long)TimeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
            span?.SetTag("duration_ms", elapsedMs);
            after?.Invoke(span, result, elapsedMs);
            return result;
        }
        catch (Exception ex)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.Message)
                 .AddException(ex);
            throw;
        }
    }

    /// <summary>Tracks a synchronous operation that returns a value.</summary>
    protected TResult TrackSync<TResult>(
        string spanName,
        Func<TResult> operation,
        Action<Activity?>? before = null,
        Action<Activity?, TResult, long>? after = null)
    {
        var startTimestamp = TimeProvider.GetTimestamp();
        using var span = ImportDiagnostics.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        before?.Invoke(span);
        try
        {
            var result = operation();
            var elapsedMs = (long)TimeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
            span?.SetTag("duration_ms", elapsedMs);
            after?.Invoke(span, result, elapsedMs);
            return result;
        }
        catch (Exception ex)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.Message)
                 .AddException(ex);
            throw;
        }
    }
}
