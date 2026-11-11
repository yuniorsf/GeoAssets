using GeoAssets.Workflow.Erp.Sienge;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the Sienge nightly-sync trigger endpoint (XD01-254) onto any
/// <see cref="IEndpointRouteBuilder"/>. "Nightly" itself is a scheduling concern outside this
/// repo's code (a cron trigger hitting this endpoint) — this endpoint only knows how to run one
/// pass and report the outcome, including any per-operation failures, so a failed run is
/// visible in the response rather than a 500 that loses the detail of which operation failed.
/// </summary>
internal static class SiengeSyncApiExtensions
{
    public static IEndpointRouteBuilder MapSiengeSyncApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/fieldservice/erp/sienge/sync")
    {
        routes.MapPost(prefix, async (
            SiengeNightlySyncOrchestrator orchestrator,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var sinceUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(-1);
            var result = await orchestrator.RunAsync(sinceUtc, ct).ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("RunSiengeNightlySync")
        .WithSummary("Runs one pass of the machine-hour/hauling-ticket cost-center posting and purchase-order reconciliation against Sienge.")
        .Produces<SiengeNightlySyncRunResult>();

        return routes;
    }
}
