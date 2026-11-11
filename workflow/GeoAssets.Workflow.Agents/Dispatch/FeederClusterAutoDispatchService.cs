using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Agents.Executors;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Dispatch;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>
/// Orchestrates XD01-240's full pass: pulls open (<see cref="ServiceOrderStatus.Draft"/>)
/// orders, clusters them by feeder via <see cref="FeederClusteringService"/>, greedily assigns
/// each cluster's orders to eligible crews (<see cref="CrewSkillMatcher"/>) nearest-depot-first,
/// routes each crew's assigned subset with <see cref="ICrewRoutingEngine"/>, and dispatches
/// every routed order through the same <see cref="DispatchServiceOrderExecutor"/> a human-driven
/// or emergency-agent flow uses — no separate dispatch code path.
/// </summary>
public sealed class FeederClusterAutoDispatchService(
    IAssetProvider          assetProvider,
    IServiceOrderRepository orderRepository,
    ICrewRoutingEngine      routingEngine,
    ServiceOrderRules       rules,
    IAgentIdentityProvider  identity,
    TimeProvider            timeProvider,
    GeoAssetsActivitySource tracer,
    ILoggerFactory          loggerFactory)
{
    public async Task<FeederClusterDispatchRunResult> DispatchOpenOrdersAsync(
        string agentId, string? agentInvocationId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);

        var openOrders = await orderRepository.GetByStatusAsync(ServiceOrderStatus.Draft, ct).ConfigureAwait(false);
        var (dispatchable, skippedNoLocation) = ProjectDispatchableOrders(openOrders);
        var byOrderId = dispatchable.ToDictionary(d => d.Order.Id, d => d, StringComparer.Ordinal);

        var clusters = FeederClusteringService.Cluster(
            [.. dispatchable.Select(d => new ServiceOrderWithLocation(d.Order.Id, d.FeatureId, d.Projection.Location))],
            assetProvider);

        var itineraries = new List<CrewItinerary>();
        var dispatchedOrderIds = new HashSet<string>(StringComparer.Ordinal);
        var unassignedOrderIds = new List<string>();
        var crews = LoadCrewRoster();
        var shiftStart = timeProvider.GetUtcNow();

        foreach (var cluster in clusters.Values)
        {
            var outcome = await DispatchClusterAsync(cluster, crews, byOrderId, agentId, agentInvocationId, shiftStart, ct)
                .ConfigureAwait(false);
            itineraries.AddRange(outcome.Itineraries);
            dispatchedOrderIds.UnionWith(outcome.DispatchedOrderIds);
            unassignedOrderIds.AddRange(outcome.UnassignedOrderIds);
        }

        return new FeederClusterDispatchRunResult(
            [.. itineraries], [.. dispatchedOrderIds], [.. unassignedOrderIds], [.. skippedNoLocation]);
    }

    private (List<DispatchableOrderEntry> Dispatchable, List<string> SkippedNoLocation) ProjectDispatchableOrders(
        IReadOnlyList<IServiceOrder> openOrders)
    {
        var dispatchable = new List<DispatchableOrderEntry>();
        var skippedNoLocation = new List<string>();

        foreach (var order in openOrders)
        {
            var concrete = (ServiceOrder)order;
            var featureId = concrete.Features.Count > 0 ? concrete.Features[0].Id : concrete.FeatureIds.FirstOrDefault();
            var feature = featureId is null
                ? null
                : concrete.Features.Count > 0 ? concrete.Features[0] : assetProvider.GetById(featureId);

            if (feature?.Geometry is null)
            {
                skippedNoLocation.Add(order.Id);
                continue;
            }

            var projection = new DispatchableOrder(
                order.Id,
                feature.Geometry.Centroid,
                ParseTags(order.Attributes.GetValueOrDefault(FeederClusterDispatchTypes.OrderRequiredSkillsAttributeKey)),
                ParseTags(order.Attributes.GetValueOrDefault(FeederClusterDispatchTypes.OrderRequiredEquipmentAttributeKey)),
                FeederClusterDispatchTypes.DefaultServiceDuration);

            dispatchable.Add(new DispatchableOrderEntry(order, projection, featureId!));
        }

        return (dispatchable, skippedNoLocation);
    }

    private async Task<ClusterDispatchOutcome> DispatchClusterAsync(
        List<ServiceOrderWithLocation> cluster,
        IReadOnlyList<CrewRosterEntry> crews,
        Dictionary<string, DispatchableOrderEntry> byOrderId,
        string agentId,
        string? agentInvocationId,
        DateTimeOffset shiftStart,
        CancellationToken ct)
    {
        var itineraries = new List<CrewItinerary>();
        var dispatchedOrderIds = new List<string>();
        var clusterCentroid = cluster[0].Location;
        var unassigned = new List<ServiceOrderWithLocation>(cluster);

        var candidateCrews = crews
            .OrderBy(c => GeodeticDistanceCalculator.DistanceInMeters(c.DepotLocation, clusterCentroid))
            .ToList();

        foreach (var crew in candidateCrews)
        {
            if (unassigned.Count == 0) break;

            var eligible = unassigned
                .Where(o => CrewSkillMatcher.IsEligible(crew, byOrderId[o.OrderId].Projection))
                .ToList();
            if (eligible.Count == 0) continue;

            var itinerary = routingEngine.BuildItinerary(
                crew.CrewFeatureId, crew.DepotLocation,
                [.. eligible.Select(o => byOrderId[o.OrderId].Projection)], shiftStart);
            itineraries.Add(itinerary);

            foreach (var stop in itinerary.Stops)
            {
                await DispatchOrderAsync(byOrderId[stop.OrderId].Order, agentId, crew.CrewFeatureId, agentInvocationId, ct)
                    .ConfigureAwait(false);
                dispatchedOrderIds.Add(stop.OrderId);
            }

            var eligibleIds = new HashSet<string>(eligible.Select(o => o.OrderId), StringComparer.Ordinal);
            unassigned.RemoveAll(o => eligibleIds.Contains(o.OrderId));
        }

        return new ClusterDispatchOutcome(itineraries, dispatchedOrderIds, [.. unassigned.Select(o => o.OrderId)]);
    }

    private async Task DispatchOrderAsync(
        IServiceOrder order, string agentId, string crewFeatureId, string? agentInvocationId, CancellationToken ct)
    {
        var dispatch = new DispatchServiceOrderExecutor(
            orderRepository, rules, identity, timeProvider, tracer,
            loggerFactory.CreateLogger<DispatchServiceOrderExecutor>());

        var workflow = new WorkflowBuilder(dispatch).WithOutputFrom(dispatch).Build();
        var message = new ServiceOrderCreated(
            (ServiceOrder)order, agentId, crewFeatureId, DispatchTargetType.Group, agentInvocationId);

        await InProcessExecution.RunAsync(workflow, message, cancellationToken: ct).ConfigureAwait(false);
    }

    private IReadOnlyList<CrewRosterEntry> LoadCrewRoster() =>
        [.. assetProvider.GetByAssetType(FeederClusterDispatchTypes.RoutineCrewAssetTypeId)
            .Where(f => f.Geometry is not null)
            .Select(f => new CrewRosterEntry(
                f.Id,
                f.Geometry!.Centroid,
                ParseTags(f.Properties.CustomAttributes.GetValueOrDefault(FeederClusterDispatchTypes.CrewSkillsAttributeKey)),
                ParseTags(f.Properties.CustomAttributes.GetValueOrDefault(FeederClusterDispatchTypes.CrewEquipmentAttributeKey))))];

    private static HashSet<string> ParseTags(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new HashSet<string>(StringComparer.Ordinal)
            : csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                 .ToHashSet(StringComparer.Ordinal);

    private sealed record DispatchableOrderEntry(IServiceOrder Order, DispatchableOrder Projection, string FeatureId);

    private sealed record ClusterDispatchOutcome(
        List<CrewItinerary> Itineraries, List<string> DispatchedOrderIds, IReadOnlyList<string> UnassignedOrderIds);
}
