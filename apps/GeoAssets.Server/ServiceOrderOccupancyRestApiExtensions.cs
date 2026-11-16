using System.Text.Json;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Services;
using GeoAssets.Server.Occupancy;
using GeoAssets.Server.Targets;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Server;

/// <summary>
/// Maps the staged-occupancy routes (XD01-315) under the same prefix as
/// <see cref="ServiceOrderTargetsRestApiExtensions"/> (default <c>/api/workflow</c>).
///
/// <list type="table">
/// <item><term>GET    /service-orders/{id}/targets/{targetId}/occupancy</term><description>the pole now, what is staged, the preview, and whether the pole moved since staging</description></item>
/// <item><term>GET    /service-orders/{id}/occupancy-changes</term><description>one page of the order's change sets (staged ones unless <c>status</c> says otherwise)</description></item>
/// <item><term>PUT    /service-orders/{id}/targets/{targetId}/occupancy-changes</term><description>stage, replacing what was staged (idempotent)</description></item>
/// <item><term>DELETE /service-orders/{id}/targets/{targetId}/occupancy-changes</term><description>discard the target's staged set</description></item>
/// <item><term>DELETE /service-orders/{id}/occupancy-changes</term><description>discard every staged set of the order</description></item>
/// </list>
///
/// <para><b>Authorization</b> is <see cref="IOrderOccupancyStagingService"/>'s: the caller is
/// the principal <see cref="ServerWorkflowPrincipalFactory"/> builds from the token (nothing in
/// a URL or body names it), the order rules must grant <c>View</c> for the reads and
/// <c>Annotate</c> for the writes — the action the target-edit routes ask of a crew — and the
/// caller must be a member of the order's organization, the pole owner. A caller the
/// organization gate refuses gets the same <c>404 orders.not-found</c> as for an unknown order;
/// a caller inside the gate whom the rules decline, or a cross-organization grantee, gets
/// <c>403 auth.denied</c>. The reads additionally require <c>serviceorders:read</c>.</para>
///
/// <para><b>Nothing here writes a pole's live occupancy.</b> Errors are
/// <c>application/problem+json</c> with a stable <c>code</c>; see <see cref="OccupancyProblemResults"/>.
/// Every handler binds the request's <see cref="CancellationToken"/> and forwards it.</para>
/// </summary>
public static class ServiceOrderOccupancyRestApiExtensions
{
    private const string ReadPolicy = "serviceorders:read";

    public static IEndpointRouteBuilder MapServiceOrderOccupancyApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/workflow")
    {
        ArgumentNullException.ThrowIfNull(routes);

        var opts = GeoJsonSerializer.Options;

        MapTargetOccupancy(routes, prefix, opts);
        MapChangeSetPage(routes, prefix, opts);
        MapStage(routes, prefix, opts);
        MapDiscard(routes, prefix, opts);

        return routes;
    }

    private static void MapTargetOccupancy(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders/{{id}}/targets/{{targetId}}/occupancy", (
            string id, string targetId, IOrderOccupancyStagingService staging,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            RunAsync(id, targetId, principalFactory, async principal =>
                Results.Json(await staging.GetAsync(id, targetId, principal, ct).ConfigureAwait(false), opts), ct))
            .RequireAuthorization(ReadPolicy);
    }

    private static void MapChangeSetPage(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders/{{id}}/occupancy-changes", (
            string id, string? page, string? pageSize, string? status, IOrderOccupancyStagingService staging,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            if (OrderTargetEndpointSupport.ReadPaging(page, pageSize, out var pageNumber, out var size) is { } pagingProblem)
                return Task.FromResult(pagingProblem);
            if (ReadStatus(status, out var wanted) is { } statusProblem)
                return Task.FromResult(statusProblem);

            return RunAsync(id, targetId: null, principalFactory, async principal =>
            {
                var result = await staging.GetPageAsync(id, pageNumber, size, wanted, principal, ct).ConfigureAwait(false);
                return Results.Json(new PagedResponse<OccupancyChangeSetView>(result.Items, pageNumber, size, result.TotalCount), opts);
            }, ct);
        }).RequireAuthorization(ReadPolicy);
    }

    /// <summary>
    /// Reads the optional <c>status</c> query value: a status name or number, or <c>all</c> for
    /// every status. Absent means the staged sets — what a crew and an approver care about.
    /// </summary>
    private static IResult? ReadStatus(string? status, out OccupancyChangeSetStatus? wanted)
    {
        wanted = OccupancyChangeSetStatus.Staged;
        if (status is null) return null;

        if (string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            wanted = null;
            return null;
        }

        if (Enum.TryParse<OccupancyChangeSetStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            wanted = parsed;
            return null;
        }

        return WorkflowProblemResults.Validation(
            $"status must be 'all' or one of: {string.Join(", ", Enum.GetNames<OccupancyChangeSetStatus>())}.");
    }

    private static void MapStage(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPut($"{prefix}/service-orders/{{id}}/targets/{{targetId}}/occupancy-changes", async (
            string id, string targetId, HttpRequest request, IOrderOccupancyStagingService staging,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var (body, _, bodyProblem) = await OrderTargetEndpointSupport
                .ReadBodyAsync<StageOccupancyChangesRequest>(request, opts, ct).ConfigureAwait(false);
            if (bodyProblem is not null) return bodyProblem;
            if (ReadChanges(body!, out var changes) is { } changesProblem) return changesProblem;

            return await RunAsync(id, targetId, principalFactory, async principal =>
                Results.Json(await staging.StageAsync(id, targetId, changes, principal, ct).ConfigureAwait(false), opts), ct)
                .ConfigureAwait(false);
        });
    }

    /// <summary>
    /// The changes of a stage request, with an id assigned to every Add that came without one.
    /// What the changes mean is the service's to judge; this only refuses a body that carries
    /// no list, or a null in it.
    /// </summary>
    private static IResult? ReadChanges(StageOccupancyChangesRequest body, out List<OccupancyChange> changes)
    {
        changes = [];
        if (body.Changes is null)
            return WorkflowProblemResults.Validation("changes is required.");

        foreach (var change in body.Changes)
        {
            if (change is null)
                return WorkflowProblemResults.Validation("changes must not contain null.");

            changes.Add(change is { Kind: OccupancyChangeKind.Add, EntryId: null }
                ? change with { EntryId = Guid.NewGuid() }
                : change);
        }

        return null;
    }

    private static void MapDiscard(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapDelete($"{prefix}/service-orders/{{id}}/targets/{{targetId}}/occupancy-changes", (
            string id, string targetId, IOrderOccupancyStagingService staging,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            RunAsync(id, targetId, principalFactory, async principal =>
            {
                var discarded = await staging.DiscardAsync(id, targetId, principal, ct).ConfigureAwait(false);
                return Results.Json(new DiscardOccupancyChangesResponse(discarded ? 1 : 0), opts);
            }, ct));

        routes.MapDelete($"{prefix}/service-orders/{{id}}/occupancy-changes", (
            string id, IOrderOccupancyStagingService staging,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            RunAsync(id, targetId: null, principalFactory, async principal =>
            {
                var discarded = await staging.DiscardAllAsync(id, principal, ct).ConfigureAwait(false);
                return Results.Json(new DiscardOccupancyChangesResponse(discarded), opts);
            }, ct));
    }

    /// <summary>
    /// Runs one service call as the caller and turns a failure the service documents into its
    /// problem body. Anything else — a cancelled request, a database failure — is not a
    /// client-facing problem and propagates.
    /// </summary>
    private static async Task<IResult> RunAsync(
        string orderId, string? targetId, ServerWorkflowPrincipalFactory principalFactory,
        Func<WorkflowPrincipal, Task<IResult>> call, CancellationToken ct)
    {
        var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
        try
        {
            return await call(principal).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && OccupancyProblemResults.FromException(ex, orderId, targetId) is { } problem)
        {
            return problem;
        }
    }
}
