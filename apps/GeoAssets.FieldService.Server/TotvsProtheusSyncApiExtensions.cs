using GeoAssets.Workflow.Erp.Totvs;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the Totvs Protheus nightly-sync trigger endpoint (XD01-253) onto any
/// <see cref="IEndpointRouteBuilder"/>. "Nightly" itself is a scheduling concern outside this
/// repo's code (a cron trigger, e.g. Hangfire or an external scheduler, hitting this endpoint)
/// — this endpoint only knows how to run one pass and report the outcome, including any
/// per-operation failures, so a failed run is visible in the response rather than a 500 that
/// loses the detail of which of the three operations actually failed.
/// </summary>
internal static class TotvsProtheusSyncApiExtensions
{
    public static IEndpointRouteBuilder MapTotvsProtheusSyncApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/fieldservice/erp/totvs/sync")
    {
        routes.MapPost(prefix, async (
            TotvsNightlySyncOrchestrator orchestrator,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var sinceUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(-1);
            var result = await orchestrator.RunAsync(sinceUtc, ct).ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("RunTotvsProtheusNightlySync")
        .WithSummary("Runs one pass of the technician-compliance pull, payroll-export push, and warehouse-stock reconciliation against Totvs Protheus.")
        .Produces<TotvsNightlySyncRunResult>();

        return routes;
    }
}
