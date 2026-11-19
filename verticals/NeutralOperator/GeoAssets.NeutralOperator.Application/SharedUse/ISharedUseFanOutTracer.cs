namespace GeoAssets.NeutralOperator.Application.SharedUse;

/// <summary>
/// Lets a host put a trace span around the creation of each child order of a fan-out, so the
/// notification staged for that child carries the span as its trace parent. The workflow core
/// references no telemetry library; the adapter lives beside the persistence adapters.
/// </summary>
public interface ISharedUseFanOutTracer
{
    /// <summary>
    /// Starts the span of one child; disposing the result ends it. Null when nothing is listening.
    /// </summary>
    IDisposable? BeginChild(string parentOrderId, string childOrderId, Guid occupantOrganizationId);
}
