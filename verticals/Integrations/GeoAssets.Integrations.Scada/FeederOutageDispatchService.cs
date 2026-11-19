using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Integrations.Scada;

/// <summary>
/// Default implementation of <see cref="IFeederOutageDispatchService"/> (XD01-239). Reads assets
/// through the narrow <see cref="IAssetTopologyQuery"/> port (XD01-386) rather than an in-process
/// <c>IAssetProvider</c>, so a vertical host answers it over the core's API. When that source is
/// unreachable the dispatch degrades instead of throwing (see <see cref="FeederOutageDispatchResult.IsDegraded"/>):
/// an emergency order is still created whenever the tripped switch itself could be read, so the
/// outage is never silently dropped.
/// </summary>
public sealed class FeederOutageDispatchService(
    IAssetTopologyQuery assetQuery,
    IServiceOrderRepository orderRepository,
    TimeProvider timeProvider) : IFeederOutageDispatchService
{
    private const string SystemActor = "system:scada-oms";

    public async Task<FeederOutageDispatchResult> DispatchForBreakerTripAsync(
        string trippedSwitchFeatureId,
        string? performedBy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(trippedSwitchFeatureId);

        var (switchFeature, switchFailure) = await ReadCoreAsync(
            token => assetQuery.GetFeatureAsync(trippedSwitchFeatureId, token), ct).ConfigureAwait(false);
        if (switchFailure is not null)
        {
            return new FeederOutageDispatchResult(
                null, trippedSwitchFeatureId, [], [], null,
                IsDegraded: true,
                DegradedReason: $"Tripped switch could not be read from the core; no order was created: {switchFailure}");
        }

        if (switchFeature is null)
            throw new KeyNotFoundException($"Feature '{trippedSwitchFeatureId}' not found.");

        var actor = performedBy ?? SystemActor;
        var now = timeProvider.GetUtcNow();

        // Resolve the isolated downstream feeder segment — the same breadth-first "feeds" walk
        // TopoGraph.GetDescendants performs, answered by the port (over the core's topology API
        // on a vertical host).
        var (descendants, impactFailure) = await ReadCoreAsync(
            token => assetQuery.GetDescendantsAsync(trippedSwitchFeatureId, ScadaOutageOrderTypes.FeedsEdgeKind, token), ct)
            .ConfigureAwait(false);
        IReadOnlyList<GeoFeature> impacted = descendants ?? [];
        var criticalFacilities = impacted
            .Where(f => !string.IsNullOrEmpty(f.Properties.CustomAttributes.GetValueOrDefault(ScadaOutageOrderTypes.CriticalFacilityAttributeKey)))
            .ToList();

        var order = BuildOrder(switchFeature, trippedSwitchFeatureId, impacted, criticalFacilities, impactFailure is null, actor, now);
        await orderRepository.AddAsync(order, ct).ConfigureAwait(false);

        var (crews, crewFailure) = await ReadCoreAsync(
            token => assetQuery.GetFeaturesByAssetTypeAsync(ScadaOutageOrderTypes.CrewAssetTypeId, token), ct)
            .ConfigureAwait(false);
        var dispatchedCrewId = crews is null ? null : FindNearestQualifiedCrew(switchFeature, crews);
        await RecordDispatchAsync(order.Id, dispatchedCrewId, actor, now, ct).ConfigureAwait(false);

        var degradedReason = (impactFailure, crewFailure) switch
        {
            (null, null) => null,
            (not null, null) => $"Downstream impact could not be resolved from the core: {impactFailure}",
            (null, not null) => $"Crew roster could not be read from the core; the order was left undispatched: {crewFailure}",
            _ => $"Downstream impact could not be resolved from the core: {impactFailure}; crew roster could not be read either, the order was left undispatched: {crewFailure}",
        };

        return new FeederOutageDispatchResult(
            order.Id,
            trippedSwitchFeatureId,
            [.. impacted.Select(f => f.Id)],
            [.. criticalFacilities.Select(f => f.Id)],
            dispatchedCrewId,
            IsDegraded: degradedReason is not null,
            DegradedReason: degradedReason);
    }

    /// <summary>
    /// Runs one core read, turning "the core is unreachable" — a <see cref="GeoCoreUnavailableException"/>,
    /// a raw <see cref="HttpRequestException"/>, or a timeout that is not the caller's own
    /// cancellation — into a failure description instead of an exception. The caller's own
    /// cancellation still propagates.
    /// </summary>
    private static async Task<(T? Value, string? Failure)> ReadCoreAsync<T>(
        Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        try
        {
            return (await read(ct).ConfigureAwait(false), null);
        }
        catch (GeoCoreUnavailableException ex)
        {
            return (default, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return (default, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return (default, ex.Message);
        }
    }

    private static ServiceOrder BuildOrder(
        GeoFeature switchFeature,
        string trippedSwitchFeatureId,
        IReadOnlyList<GeoFeature> impacted,
        List<GeoFeature> criticalFacilities,
        bool impactResolved,
        string actor,
        DateTimeOffset now) => new()
    {
        OrderTypeId = ScadaOutageOrderTypes.EmergencyOutageDispatchId,
        Title = $"Emergency outage — switch {switchFeature.Properties.Name} tripped",
        Description = !impactResolved
            ? "Breaker trip reported; the downstream impact could not be resolved (core asset source unavailable) — assess the feeder manually."
            : criticalFacilities.Count > 0
                ? $"Breaker trip isolates {impacted.Count} downstream feature(s), including {criticalFacilities.Count} critical facility/facilities."
                : $"Breaker trip isolates {impacted.Count} downstream feature(s).",
        Priority = ServiceOrderPriority.Critical,
        CreatedBy = actor,
        CreatedAt = now.UtcDateTime,
        OrganizationId = switchFeature.Properties.OrganizationId,
        FeatureIds = [trippedSwitchFeatureId, .. impacted.Select(f => f.Id)],
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ScadaOutageOrderTypes.TrippedSwitchAttributeKey] = trippedSwitchFeatureId,
            [ScadaOutageOrderTypes.CriticalFacilityCountAttributeKey] = criticalFacilities.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
    };

    private async Task RecordDispatchAsync(
        string orderId, string? dispatchedCrewId, string actor, DateTimeOffset now, CancellationToken ct)
    {
        if (dispatchedCrewId is not null)
        {
            await orderRepository.AppendDispatchAsync(
                orderId,
                new OrderDispatch(dispatchedCrewId, DispatchTargetType.Group, actor, now.UtcDateTime,
                    "Auto-dispatched to nearest qualified crew on SCADA breaker-trip alarm")
                {
                    ActorKind = ActorKind.System,
                },
                ct).ConfigureAwait(false);
        }

        await orderRepository.AppendActionAsync(
            orderId,
            new OrderActionLog(
                OrderAction.Dispatch,
                actor,
                now.UtcDateTime,
                dispatchedCrewId is not null
                    ? $"Auto-created emergency outage order; dispatched to crew '{dispatchedCrewId}'"
                    : "Auto-created emergency outage order; no qualified crew found within search radius"),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the nearest crew feature flagged qualified for emergency dispatch, by straight-line
    /// distance from the tripped switch, within <see cref="ScadaOutageOrderTypes.CrewSearchRadiusDegrees"/>
    /// — the same degree-distance contract <c>IAssetProvider.GetNearby</c> applied before XD01-386,
    /// now ranked here over the crew roster since the core has no spatial-query endpoint.
    /// Deliberately simple nearest-match, not an optimized multi-crew assignment — that's
    /// XD01-240/256's feeder-clustering/VRPTW routing engine, which this ticket is a dependency of,
    /// not a duplicate of.
    /// </summary>
    private static string? FindNearestQualifiedCrew(GeoFeature switchFeature, IReadOnlyList<GeoFeature> crews)
    {
        if (switchFeature.Geometry is null) return null;

        var center = switchFeature.Geometry.Centroid;
        return crews
            .Where(f =>
                f.Geometry is not null &&
                string.Equals(f.Properties.AssetTypeId, ScadaOutageOrderTypes.CrewAssetTypeId, StringComparison.Ordinal) &&
                string.Equals(f.Properties.CustomAttributes.GetValueOrDefault(ScadaOutageOrderTypes.EmergencyQualifiedAttributeKey), "true", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Feature: f, Distance: f.Geometry!.Distance(center)))
            .Where(c => c.Distance <= ScadaOutageOrderTypes.CrewSearchRadiusDegrees)
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Feature.Id, StringComparer.Ordinal)
            .Select(c => c.Feature.Id)
            .FirstOrDefault();
    }
}
