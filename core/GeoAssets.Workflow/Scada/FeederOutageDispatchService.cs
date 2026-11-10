using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Scada;

/// <summary>Default implementation of <see cref="IFeederOutageDispatchService"/> (XD01-239).</summary>
public sealed class FeederOutageDispatchService(
    IAssetProvider assetProvider,
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

        var switchFeature = assetProvider.GetById(trippedSwitchFeatureId)
            ?? throw new KeyNotFoundException($"Feature '{trippedSwitchFeatureId}' not found.");

        var actor = performedBy ?? SystemActor;
        var now = timeProvider.GetUtcNow();

        // Resolve the isolated downstream feeder segment via the existing TopoGraph BFS
        // traversal — same algorithm the <30ms/≥3,000-node performance contract is measured
        // against directly in GeoAssets.Core.Tests (no separate graph implementation here).
        var impacted = assetProvider.GetDescendants(trippedSwitchFeatureId, ScadaOutageOrderTypes.FeedsEdgeKind);
        var criticalFacilities = impacted
            .Where(f => !string.IsNullOrEmpty(f.Properties.CustomAttributes.GetValueOrDefault(ScadaOutageOrderTypes.CriticalFacilityAttributeKey)))
            .ToList();

        var order = BuildOrder(switchFeature, trippedSwitchFeatureId, impacted, criticalFacilities, actor, now);
        await orderRepository.AddAsync(order, ct).ConfigureAwait(false);

        var dispatchedCrewId = FindNearestQualifiedCrew(switchFeature);
        await RecordDispatchAsync(order.Id, dispatchedCrewId, actor, now, ct).ConfigureAwait(false);

        return new FeederOutageDispatchResult(
            order.Id,
            trippedSwitchFeatureId,
            [.. impacted.Select(f => f.Id)],
            [.. criticalFacilities.Select(f => f.Id)],
            dispatchedCrewId);
    }

    private static ServiceOrder BuildOrder(
        GeoFeature switchFeature,
        string trippedSwitchFeatureId,
        IReadOnlyList<GeoFeature> impacted,
        List<GeoFeature> criticalFacilities,
        string actor,
        DateTimeOffset now) => new()
    {
        OrderTypeId = ScadaOutageOrderTypes.EmergencyOutageDispatchId,
        Title = $"Emergency outage — switch {switchFeature.Properties.Name} tripped",
        Description = criticalFacilities.Count > 0
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
                OrderActionType.Dispatch,
                actor,
                now.UtcDateTime,
                dispatchedCrewId is not null
                    ? $"Auto-created emergency outage order; dispatched to crew '{dispatchedCrewId}'"
                    : "Auto-created emergency outage order; no qualified crew found within search radius"),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the nearest crew feature flagged qualified for emergency dispatch, by straight-line
    /// distance from the tripped switch (<see cref="IAssetProvider.GetNearby"/> already returns
    /// ascending-distance order). Deliberately simple nearest-match, not an optimized multi-crew
    /// assignment — that's XD01-240/256's feeder-clustering/VRPTW routing engine, which this
    /// ticket is a dependency of, not a duplicate of.
    /// </summary>
    private string? FindNearestQualifiedCrew(GeoFeature switchFeature)
    {
        if (switchFeature.Geometry is null) return null;

        return assetProvider
            .GetNearby(switchFeature.Geometry.Centroid, ScadaOutageOrderTypes.CrewSearchRadiusDegrees)
            .FirstOrDefault(f =>
                string.Equals(f.Properties.AssetTypeId, ScadaOutageOrderTypes.CrewAssetTypeId, StringComparison.Ordinal) &&
                string.Equals(f.Properties.CustomAttributes.GetValueOrDefault(ScadaOutageOrderTypes.EmergencyQualifiedAttributeKey), "true", StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }
}
