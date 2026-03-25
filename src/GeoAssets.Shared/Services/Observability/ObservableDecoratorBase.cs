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

    protected ObservableDecoratorBase(ILogger<T> logger) => Logger = logger;

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
        var sw = Stopwatch.StartNew();
        using var span = ImportDiagnostics.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        before?.Invoke(span);
        try
        {
            await operation();
            sw.Stop();
            span?.SetTag("duration_ms", sw.ElapsedMilliseconds);
            after?.Invoke(span, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
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
        var sw = Stopwatch.StartNew();
        using var span = ImportDiagnostics.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        before?.Invoke(span);
        try
        {
            var result = await operation();
            sw.Stop();
            span?.SetTag("duration_ms", sw.ElapsedMilliseconds);
            after?.Invoke(span, result, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
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
        var sw = Stopwatch.StartNew();
        using var span = ImportDiagnostics.ActivitySource.StartActivity(spanName, ActivityKind.Internal);
        before?.Invoke(span);
        try
        {
            var result = operation();
            sw.Stop();
            span?.SetTag("duration_ms", sw.ElapsedMilliseconds);
            after?.Invoke(span, result, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            span?.SetStatus(ActivityStatusCode.Error, ex.Message)
                 .AddException(ex);
            throw;
        }
    }
}
