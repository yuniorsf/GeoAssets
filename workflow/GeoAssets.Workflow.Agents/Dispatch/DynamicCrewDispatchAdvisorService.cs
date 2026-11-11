using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Dispatch;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Agents.Dispatch;

/// <summary>
/// XD01-256's day-to-day, yield-optimized dispatch advisor — distinct from XD01-239/240's
/// SCADA-triggered emergency path (<c>FeederClusterAutoDispatchService</c>): this one runs over
/// every open (<see cref="ServiceOrderStatus.Draft"/>) routine service order, ranks them by
/// predicted US value via <see cref="OrderYieldRanker"/> (never deprioritizing an order whose
/// <c>slaDeadlineUtc</c> attribute — see <c>GeoAssets.Workflow.SlaGuard.SlaGuardEngine</c> — is
/// imminent), and routes each crew's assignable subset with the same <see cref="ICrewRoutingEngine"/>
/// XD01-240 built (reused as-is, not reimplemented). Unlike that service, this one never calls a
/// dispatch executor — its output is a <see cref="DynamicDispatchAdvisoryResult"/> for a human
/// dispatcher to accept, adjust, or discard (this ticket's AC).
///
/// Predicted value comes from <see cref="OrderRemunerationCalculator"/> run against whatever
/// planned BoQ line list is stamped on <see cref="ServiceOrderBoqAttributes.BoqLinesKey"/> at
/// order-creation time — the same attribute convention <see cref="ServiceOrderBoqAttributes"/>
/// documents for a *posted* (after-the-fact) BoQ, reused here for a *planned* (before-the-fact)
/// estimate, since this codebase has no separate yield-forecasting model. An order with no
/// planned BoQ attribute predicts at 0 US — it still gets routed, it simply never outranks a
/// valued order on non-urgent ranking.
/// </summary>
public sealed class DynamicCrewDispatchAdvisorService(
    IAssetProvider assetProvider,
    IServiceOrderRepository orderRepository,
    ICrewRoutingEngine routingEngine,
    OrderRemunerationCalculator remunerationCalculator,
    TimeProvider timeProvider)
{
    /// <summary>An order due within this long is ranked ahead of every non-urgent order,
    /// regardless of predicted value — the default window a caller can override.</summary>
    public static readonly TimeSpan DefaultDeadlineUrgencyWindow = TimeSpan.FromHours(24);

    public async Task<DynamicDispatchAdvisoryResult> GenerateAdvisoryPlanAsync(
        TimeSpan? deadlineUrgencyWindow = null, CancellationToken ct = default)
    {
        var urgencyWindow = deadlineUrgencyWindow ?? DefaultDeadlineUrgencyWindow;
        var now = timeProvider.GetUtcNow();

        var openOrders = await orderRepository.GetByStatusAsync(ServiceOrderStatus.Draft, ct).ConfigureAwait(false);
        var (ranked, skippedNoLocation) = await ProjectAndRankAsync(openOrders, ct).ConfigureAwait(false);

        var orderedRanked = OrderYieldRanker.Rank(ranked, now, urgencyWindow);
        var deadlineCriticalIds = orderedRanked
            .Where(o => OrderYieldRanker.IsUrgent(o, now, urgencyWindow))
            .Select(o => o.Order.OrderId)
            .ToList();

        var (itineraries, optimizedDistance, baselineDistance, unassignedIds) =
            AssignCrews(orderedRanked, LoadCrewRoster(), now);

        return new DynamicDispatchAdvisoryResult(
            itineraries,
            [.. orderedRanked.Select(o => o.Order.OrderId)],
            deadlineCriticalIds,
            unassignedIds,
            skippedNoLocation,
            optimizedDistance,
            baselineDistance);
    }

    private (
        List<CrewItinerary> Itineraries,
        double OptimizedDistanceMeters,
        double BaselineDistanceMeters,
        List<string> UnassignedOrderIds) AssignCrews(
            IReadOnlyList<YieldRankedOrder> rankedOrders, IReadOnlyList<CrewRosterEntry> crews, DateTimeOffset shiftStart)
    {
        var itineraries = new List<CrewItinerary>();
        var remaining = new List<YieldRankedOrder>(rankedOrders);
        var availableCrews = new List<CrewRosterEntry>(crews);
        var optimizedDistance = 0d;
        var baselineDistance = 0d;

        while (remaining.Count > 0 && availableCrews.Count > 0)
        {
            var next = remaining[0];
            var crew = availableCrews
                .Where(c => CrewSkillMatcher.IsEligible(c, next.Order))
                .OrderBy(c => GeodeticDistanceCalculator.DistanceInMeters(c.DepotLocation, next.Order.Location))
                .FirstOrDefault();

            if (crew is null)
            {
                // No remaining crew can serve the next-ranked order's requirements at all —
                // it (and only it) is unassignable; leave it and keep assigning the rest.
                remaining.RemoveAt(0);
                continue;
            }

            var crewOrders = remaining.Where(o => CrewSkillMatcher.IsEligible(crew, o.Order)).Select(o => o.Order).ToList();
            var itinerary = routingEngine.BuildItinerary(crew.CrewFeatureId, crew.DepotLocation, crewOrders, shiftStart);
            itineraries.Add(itinerary);
            optimizedDistance += itinerary.TotalDistanceMeters;
            baselineDistance += NearestNeighborBaselineRouter.ComputeTotalDistanceMeters(crew.DepotLocation, crewOrders);

            var assignedIds = new HashSet<string>(itinerary.Stops.Select(s => s.OrderId), StringComparer.Ordinal);
            remaining.RemoveAll(o => assignedIds.Contains(o.Order.OrderId));
            availableCrews.Remove(crew);
        }

        return (itineraries, optimizedDistance, baselineDistance, [.. remaining.Select(o => o.Order.OrderId)]);
    }

    private async Task<(List<YieldRankedOrder> Ranked, List<string> SkippedNoLocation)> ProjectAndRankAsync(
        IReadOnlyList<IServiceOrder> openOrders, CancellationToken ct)
    {
        var ranked = new List<YieldRankedOrder>();
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
                FeederClusterDispatchTypes.DefaultServiceDuration,
                ReadSlaDeadline(order.Attributes));

            var predictedValueUs = await PredictValueAsync(order, ct).ConfigureAwait(false);
            ranked.Add(new YieldRankedOrder(projection, predictedValueUs));
        }

        return (ranked, skippedNoLocation);
    }

    private async Task<decimal> PredictValueAsync(IServiceOrder order, CancellationToken ct)
    {
        var lines = ServiceOrderBoqAttributes.ReadBoqLines(order.Attributes);
        if (lines.Count == 0)
        {
            return 0m;
        }

        try
        {
            var result = await remunerationCalculator
                .CalculateAsync(order.OrganizationId, lines, timeProvider.GetUtcNow().UtcDateTime, ct)
                .ConfigureAwait(false);
            return result.TotalUs;
        }
        catch (UsCatalogItemNotFoundException)
        {
            return 0m;
        }
        catch (AmbiguousFinancialConversionRateException)
        {
            return 0m;
        }
    }

    private IReadOnlyList<CrewRosterEntry> LoadCrewRoster() =>
        [.. assetProvider.GetByAssetType(FeederClusterDispatchTypes.RoutineCrewAssetTypeId)
            .Where(f => f.Geometry is not null)
            .Select(f => new CrewRosterEntry(
                f.Id,
                f.Geometry!.Centroid,
                ParseTags(f.Properties.CustomAttributes.GetValueOrDefault(FeederClusterDispatchTypes.CrewSkillsAttributeKey)),
                ParseTags(f.Properties.CustomAttributes.GetValueOrDefault(FeederClusterDispatchTypes.CrewEquipmentAttributeKey))))];

    private static DateTimeOffset? ReadSlaDeadline(IReadOnlyDictionary<string, string> attributes) =>
        attributes.TryGetValue(GeoAssets.Workflow.SlaGuard.SlaGuardEngine.DeadlineAttributeKey, out var raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deadline)
            ? deadline
            : null;

    private static HashSet<string> ParseTags(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new HashSet<string>(StringComparer.Ordinal)
            : csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                 .ToHashSet(StringComparer.Ordinal);
}
