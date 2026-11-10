namespace GeoAssets.Workflow.Scada;

/// <summary>
/// Consumes a SCADA breaker-trip alarm (IEC 60870-5-104/DNP3 ingest is assumed external to this
/// boundary — this interface starts at "a switch feature ID tripped") and resolves the isolated
/// feeder segment via <see cref="Core.Services.TopoGraph"/> traversal, auto-creating a
/// high-priority emergency <see cref="Orders.ServiceOrder"/> routed to the nearest qualified
/// crew (XD01-239).
/// </summary>
public interface IFeederOutageDispatchService
{
    /// <param name="trippedSwitchFeatureId">ID of the switch/breaker feature SCADA/OMS reported
    /// as tripped.</param>
    /// <param name="performedBy">Attributed actor for the audit log; defaults to a well-known
    /// system identity since this path is always machine-triggered.</param>
    Task<FeederOutageDispatchResult> DispatchForBreakerTripAsync(
        string trippedSwitchFeatureId,
        string? performedBy = null,
        CancellationToken ct = default);
}
