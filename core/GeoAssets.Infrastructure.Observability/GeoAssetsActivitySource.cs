using System.Diagnostics;
using OpenTelemetry.Trace;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// Centralised <see cref="ActivitySource"/> for GeoAssets domain operations.
///
/// Inject <see cref="GeoAssetsActivitySource"/> and call
/// <see cref="StartActivity"/> to create custom spans that appear alongside
/// ASP.NET Core and HTTP client spans in the configured OTLP backend
/// (New Relic as of XD01-30).
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
public sealed class GeoAssetsActivitySource : IDisposable
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

    public Activity? StartNotificationActivity(string orderId, string transport, ActivityKind kind = ActivityKind.Internal) =>
        _source.StartActivity("Notification.Publish", kind)
               ?.AddTag("order.id", orderId)
                .AddTag("messaging.system", transport);

    /// <summary>
    /// Starts a span for an AI-agent-driven workflow operation (see
    /// <c>GeoAssets.Workflow.Agents.Executors</c>), tagged with the order, the acting agent,
    /// and — when present — the agent invocation/run that triggered it.
    /// </summary>
    public Activity? StartAgentActivity(string operationName, string orderId, string agentId, string? agentInvocationId = null) =>
        _source.StartActivity($"Agent.{operationName}")
               ?.AddTag("order.id", orderId)
                .AddTag("agent.id", agentId)
                .AddTag("agent.invocation_id", agentInvocationId);

    // ── Integration Messaging ─────────────────────────────────────────────────

    public Activity? StartIntegrationPublishActivity(
        string messageType,
        Guid messageId,
        string transport,
        string destination,
        string? catalogInstanceId = null,
        ActivityContext parentContext = default)
    {
        var activity = _source.StartActivity($"{destination} publish", ActivityKind.Producer, parentContext);
        if (activity is null) return null;

        activity.SetTag("messaging.system", transport);
        activity.SetTag("messaging.operation.name", "publish");
        activity.SetTag("messaging.destination.name", destination);
        activity.SetTag("messaging.message.id", messageId.ToString("D"));
        activity.SetTag("geoassets.message.type", messageType);
        if (catalogInstanceId is not null)
        {
            activity.SetTag("geoassets.catalog.id", catalogInstanceId);
        }

        return activity;
    }

    public Activity? StartIntegrationConsumeActivity(
        string messageType,
        Guid messageId,
        string transport,
        string destination,
        string consumerGroup,
        string? catalogInstanceId = null,
        ActivityContext parentContext = default)
    {
        var activity = _source.StartActivity($"{destination} process", ActivityKind.Consumer, parentContext);
        if (activity is null) return null;

        activity.SetTag("messaging.system", transport);
        activity.SetTag("messaging.operation.name", "process");
        activity.SetTag("messaging.destination.name", destination);
        activity.SetTag("messaging.consumer.group.name", consumerGroup);
        activity.SetTag("messaging.message.id", messageId.ToString("D"));
        activity.SetTag("geoassets.message.type", messageType);
        if (catalogInstanceId is not null)
        {
            activity.SetTag("geoassets.catalog.id", catalogInstanceId);
        }

        return activity;
    }

    public static void InjectTraceContext<TCarrier>(
        Activity? activity,
        TCarrier carrier,
        Action<TCarrier, string, string> setter)
    {
        ArgumentNullException.ThrowIfNull(setter);
        if (activity is null || carrier is null) return;

        DistributedContextPropagator.Current.Inject(
            activity,
            carrier,
            (target, key, value) =>
            {
                if (target is TCarrier typed && !string.IsNullOrEmpty(key) && value is not null)
                {
                    setter(typed, key, value);
                }
            });
    }

    public static ActivityContext ExtractParentContext<TCarrier>(
        TCarrier carrier,
        DistributedContextPropagator.PropagatorGetterCallback getter,
        bool isRemote = true)
    {
        ArgumentNullException.ThrowIfNull(getter);
        if (carrier is null) return default;

        DistributedContextPropagator.Current.ExtractTraceIdAndState(
            carrier,
            getter,
            out var traceParent,
            out var traceState);

        return ActivityContext.TryParse(traceParent, traceState, isRemote, out var ctx)
            ? ctx
            : default;
    }

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
        ArgumentNullException.ThrowIfNull(ex);
        if (activity is null) return;
        activity.AddException(ex);
        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
    }

    public void Dispose() => _source.Dispose();
}
