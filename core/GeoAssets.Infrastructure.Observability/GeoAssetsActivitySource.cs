using System.Diagnostics;
using OpenTelemetry.Trace;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// Centralised <see cref="ActivitySource"/> for GeoAssets domain operations.
///
/// Inject <see cref="GeoAssetsActivitySource"/> and call
/// <see cref="StartActivity"/> to create custom spans that appear alongside
/// ASP.NET Core and HTTP client spans in Azure Monitor / Application Insights.
///
/// <code>
/// public class ServiceOrderCommandHandler(GeoAssetsActivitySource tracer, …)
/// {
///     public async Task TransitionAsync(string orderId, …)
///     {
///         using var span = tracer.StartActivity("ServiceOrder.Transition")
///             ?.AddTag("order.id", orderId)
///              .AddTag("order.newStatus", newStatus.ToString());
///         …
///     }
/// }
/// </code>
/// </summary>
public sealed class GeoAssetsActivitySource
{
    /// <summary>Name registered with OpenTelemetry so spans appear under a consistent source.</summary>
    public const string SourceName = "GeoAssets";

    private readonly ActivitySource _source;

    public GeoAssetsActivitySource(string version)
        => _source = new ActivitySource(SourceName, version);

    // ── Workflow ──────────────────────────────────────────────────────────────

    public Activity? StartOrderActivity(string operationName, string orderId) =>
        _source.StartActivity($"ServiceOrder.{operationName}")
               ?.AddTag("order.id", orderId);

    public Activity? StartNotificationActivity(string orderId, string transport) =>
        _source.StartActivity("Notification.Publish")
               ?.AddTag("order.id", orderId)
                .AddTag("messaging.system", transport);

    // ── Generic ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a named activity with <see cref="ActivityKind.Internal"/>.
    /// Returns null when the source has no active listeners (zero overhead).
    /// </summary>
    public Activity? StartActivity(
        string name,
        ActivityKind kind = ActivityKind.Internal,
        ActivityContext parentContext = default)
        => _source.StartActivity(name, kind, parentContext);

    /// <summary>Records an exception on the current <see cref="Activity"/> and sets its status to Error.</summary>
    public static void RecordException(Activity? activity, Exception ex)
    {
        if (activity is null) return;
        activity.AddException(ex);
        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
    }
}
