using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authentication;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.FieldWork;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// CTO splitter port capacity tracking & occupancy heatmap (XD01-281), built on XD01-279's ISP
/// vertical host scaffolding. The performing user comes from the authenticated principal
/// (<see cref="ICurrentUserAccessor"/>), never the request body, same audit-critical-action
/// convention <see cref="FieldWorkInspectionRestApiExtensions"/> documents for its own endpoint.
///
/// Also records XD01-287's CTO occupancy-discrepancy KPI
/// (<see cref="GeoAssetsMeter.RecordCtoOccupancyDiscrepancy"/>) whenever a reconciliation
/// finds one — the magnitude only, never the CTO feature id, per the "no high-cardinality
/// label on a metric" rule (per-CTO drill-down belongs in a trace/log attribute, not here).
/// </summary>
internal static class IspCtoOccupancyRestApiExtensions
{
    public static IEndpointRouteBuilder MapIspCtoOccupancyApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/isp/cto")
    {
        routes.MapPost($"{prefix}/{{id}}/occupancy/adjust", AdjustOccupancyAsync)
            .WithName("AdjustCtoOccupancy")
            .WithSummary("Installs/removes a drop at a CTO, atomically updating its occupied splitter port count via a single-item mutation batch.")
            .Produces<CtoOccupancyAdjustResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        routes.MapGet($"{prefix}/occupancy/heatmap", GetHeatmapAsync)
            .WithName("GetCtoOccupancyHeatmap")
            .WithSummary("Returns every CTO at or above the given occupancy percentage threshold — exhausted distribution zones for network-expansion planning.")
            .Produces<IReadOnlyList<CtoOccupancyHeatmapEntry>>();

        routes.MapPost($"{prefix}/{{id}}/occupancy/reconcile", ReconcileOccupancyAsync)
            .WithName("ReconcileCtoOccupancy")
            .WithSummary("Compares a BSS-reported occupancy count against GeoAssets' own tracked count and reports any discrepancy — never silently overwrites either side.")
            .Produces<CtoOccupancyDiscrepancy>()
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> AdjustOccupancyAsync(
        string id,
        CtoOccupancyAdjustRequest request,
        IGeoCoreClient geoCoreClient,
        ITenantContext tenantContext,
        ICurrentUserAccessor currentUserAccessor,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var feature = await geoCoreClient.GetFeatureByIdAsync(id, ct).ConfigureAwait(false);
        if (feature is null)
            return Results.NotFound();

        var adjustment = CtoOccupancyMutationFactory.BuildAdjustmentMutation(feature, request.Delta);
        if (!adjustment.Succeeded)
            return Results.BadRequest(adjustment.FailureReason);

        var currentUser = await currentUserAccessor.GetCurrentUserAsync(ct).ConfigureAwait(false);
        var performedByUserId = currentUser?.ExternalObjectId;
        if (string.IsNullOrEmpty(performedByUserId))
            return Results.Unauthorized();

        var batch = AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: request.CatalogInstanceId,
            organizationId: tenantContext.CurrentOrganizationId,
            requestedByService: "GeoAssets.FieldService.Server.Isp",
            performedByUserId: performedByUserId,
            mutations: [adjustment.Mutation!],
            timeProvider: timeProvider);

        await geoCoreClient.ExecuteAssetDeltaBatchAsync(batch, ct).ConfigureAwait(false);

        var (_, total) = CtoCapacityCalculator.GetOccupiedAndTotal(feature.Properties.CustomAttributes);
        var percentage = total <= 0 ? 0 : 100.0 * adjustment.NewOccupiedCount!.Value / total;
        return Results.Ok(new CtoOccupancyAdjustResponse(adjustment.NewOccupiedCount!.Value, total, percentage));
    }

    private static async Task<IResult> GetHeatmapAsync(
        double thresholdPercentage,
        IGeoCoreClient geoCoreClient,
        CancellationToken ct)
    {
        var page = await geoCoreClient.QueryFeaturesAsync(
            new AssetQuery { AssetTypeId = IspAssetTypeIds.Cto.ToString(), Take = 1000 }, ct).ConfigureAwait(false);
        var heatmap = CtoOccupancyHeatmapQuery.GetAboveThreshold(page.Items, thresholdPercentage);
        return Results.Ok(heatmap);
    }

    private static async Task<IResult> ReconcileOccupancyAsync(
        string id,
        CtoOccupancyReconcileRequest request,
        IGeoCoreClient geoCoreClient,
        GeoAssetsMeter metrics,
        CancellationToken ct)
    {
        var feature = await geoCoreClient.GetFeatureByIdAsync(id, ct).ConfigureAwait(false);
        if (feature is null)
            return Results.NotFound();

        var (occupied, _) = CtoCapacityCalculator.GetOccupiedAndTotal(feature.Properties.CustomAttributes);
        var discrepancy = CtoOccupancyDiscrepancyDetector.Detect(id, request.BssReportedOccupied, occupied);

        if (discrepancy.HasDiscrepancy)
            metrics.RecordCtoOccupancyDiscrepancy(discrepancy.Difference);

        return Results.Ok(discrepancy);
    }
}
