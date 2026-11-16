using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Server.Targets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Server;

/// <summary>
/// Maps the service-order target and selection routes (XD01-308) under the same prefix as
/// <see cref="ServiceOrdersRestApiExtensions"/> (default <c>/api/workflow</c>).
///
/// <list type="table">
/// <item><term>GET  /service-orders/{id}/targets</term><description>one page of targets (R1)</description></item>
/// <item><term>GET  /service-orders/{id}/targets/count</term><description>how many targets (R2)</description></item>
/// <item><term>GET  /service-orders/{id}/targets/extent</term><description>their bounding box; 204 when none is positioned (R3)</description></item>
/// <item><term>GET  /service-orders/{id}/targets/{targetId}</term><description>one target</description></item>
/// <item><term>POST /service-orders/targets/usages</term><description>which features other open orders hold (R4)</description></item>
/// <item><term>POST /service-orders/{id}/targets/selection/preview</term><description>what a selection would add (R5)</description></item>
/// <item><term>POST /service-orders/{id}/targets/selection/apply</term><description>add the selection (R6)</description></item>
/// <item><term>PATCH /service-orders/{id}/targets/{targetId}</term><description>activities, status, note (R7)</description></item>
/// <item><term>POST /service-orders/{id}/targets/remove</term><description>remove targets (R8)</description></item>
/// <item><term>POST /selection/resolve</term><description>map gesture → feature ids of the caller's organization</description></item>
/// </list>
///
/// <para><b>Authorization.</b> The caller is always the principal
/// <see cref="ServerWorkflowPrincipalFactory"/> builds from the token; no route reads it from the
/// URL or the body. Reads require <c>serviceorders:read</c> and, because the target store's
/// per-order members ignore the tenant filter, an explicit check that the caller may see that
/// order (see <see cref="OrderTargetEndpointSupport.ForReadAsync"/>). Edits evaluate
/// <see cref="ServiceOrderRules"/> for <see cref="OrderActionType.Annotate"/>. The selection
/// routes leave the rule check to <see cref="IOrderTargetSelectionService"/> (View for preview,
/// Annotate for apply), which runs it before touching any target.</para>
///
/// <para><b>Errors</b> are <c>application/problem+json</c> with a stable <c>code</c>
/// (<see cref="WorkflowProblemCodes"/>); see <see cref="WorkflowProblemResults"/>. There is no
/// route that adds targets directly: a target enters an order only through a selection.</para>
///
/// Every handler binds the request's <see cref="CancellationToken"/> and forwards it.
/// </summary>
public static partial class ServiceOrderTargetsRestApiExtensions
{
    private const string ReadPolicy = "serviceorders:read";

    public static IEndpointRouteBuilder MapServiceOrderTargetsApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/workflow")
    {
        ArgumentNullException.ThrowIfNull(routes);

        var opts = GeoJsonSerializer.Options;

        MapTargetPage(routes, prefix, opts);
        MapTargetCountAndExtent(routes, prefix, opts);
        MapTargetById(routes, prefix, opts);
        MapTargetUsages(routes, prefix, opts);
        MapSelectionPreview(routes, prefix, opts);
        MapSelectionApply(routes, prefix, opts);
        MapTargetUpdate(routes, prefix, opts);
        MapTargetRemove(routes, prefix, opts);
        MapSelectionResolve(routes, prefix, opts);

        return routes;
    }

    // ── Reads ────────────────────────────────────────────────────────────────

    private static void MapTargetPage(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders/{{id}}/targets", async (
            string id, string? page, string? pageSize, string? sortBy, string? descending, string? status, string? conflictLevel,
            IServiceOrderRepository orders, IServiceOrderTargetReader targets,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            if (OrderTargetEndpointSupport.ReadPaging(page, pageSize, out var pageNumber, out var size) is { } pagingProblem)
                return pagingProblem;
            if (ReadFilters(descending, status, conflictLevel, out var isDescending, out var level) is { } filterProblem)
                return filterProblem;

            var access = await OrderTargetEndpointSupport.ForReadAsync(id, orders, rules, principalFactory, ct).ConfigureAwait(false);
            if (access.Problem is not null) return access.Problem;

            var result = await targets
                .GetPageAsync(new OrderTargetQuery(id, pageNumber, size, sortBy, isDescending, status, level), ct)
                .ConfigureAwait(false);

            return Results.Json(new PagedResponse<ServiceOrderTarget>(result.Items, pageNumber, size, result.TotalCount), opts);
        }).RequireAuthorization(ReadPolicy);
    }

    /// <summary>
    /// Reads the optional <c>descending</c>, <c>status</c> and <c>conflictLevel</c> query values.
    /// An unknown sort key is not an error (the store falls back to feature id); an unknown
    /// status or conflict level is, because it can only ever match nothing.
    /// </summary>
    private static IResult? ReadFilters(
        string? descending, string? status, string? conflictLevel, out bool isDescending, out OrderTargetConflictLevel? level)
    {
        isDescending = false;
        level = null;

        if (descending is not null && !bool.TryParse(descending, out isDescending))
            return WorkflowProblemResults.Validation("descending must be true or false.");

        if (status is not null && !OrderTargetStatus.IsDefined(status))
            return WorkflowProblemResults.Validation($"status must be one of: {string.Join(", ", OrderTargetStatus.All)}.");

        if (conflictLevel is not null)
        {
            if (!Enum.TryParse<OrderTargetConflictLevel>(conflictLevel, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return WorkflowProblemResults.Validation($"conflictLevel must be one of: {string.Join(", ", Enum.GetNames<OrderTargetConflictLevel>())}.");

            level = parsed;
        }

        return null;
    }

    private static void MapTargetCountAndExtent(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders/{{id}}/targets/count", async (
            string id, IServiceOrderRepository orders, IServiceOrderTargetReader targets,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var access = await OrderTargetEndpointSupport.ForReadAsync(id, orders, rules, principalFactory, ct).ConfigureAwait(false);
            if (access.Problem is not null) return access.Problem;

            var count = await targets.CountAsync(id, ct).ConfigureAwait(false);
            return Results.Json(new TargetCountResponse(count), opts);
        }).RequireAuthorization(ReadPolicy);

        routes.MapGet($"{prefix}/service-orders/{{id}}/targets/extent", async (
            string id, IServiceOrderRepository orders, IServiceOrderTargetReader targets,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var access = await OrderTargetEndpointSupport.ForReadAsync(id, orders, rules, principalFactory, ct).ConfigureAwait(false);
            if (access.Problem is not null) return access.Problem;

            var extent = await targets.GetExtentAsync(id, ct).ConfigureAwait(false);
            return extent is null
                ? Results.NoContent()
                : Results.Json(
                    new TargetExtentResponse([extent.MinLongitude, extent.MinLatitude, extent.MaxLongitude, extent.MaxLatitude]), opts);
        }).RequireAuthorization(ReadPolicy);
    }

    private static void MapTargetById(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders/{{id}}/targets/{{targetId}}", async (
            string id, string targetId, IServiceOrderRepository orders, IServiceOrderTargetReader targets,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var access = await OrderTargetEndpointSupport.ForReadAsync(id, orders, rules, principalFactory, ct).ConfigureAwait(false);
            if (access.Problem is not null) return access.Problem;

            var target = await targets.GetByIdAsync(id, targetId, ct).ConfigureAwait(false);
            return target is null
                ? WorkflowProblemResults.TargetNotFound(id, targetId)
                : Results.Json(target, opts);
        }).RequireAuthorization(ReadPolicy);
    }

    private static void MapTargetUsages(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/service-orders/targets/usages", async (
            HttpRequest request, IServiceOrderRepository orders, IServiceOrderTargetReader targets,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var (body, _, bodyProblem) = await OrderTargetEndpointSupport
                .ReadBodyAsync<TargetUsagesRequest>(request, opts, ct).ConfigureAwait(false);
            if (bodyProblem is not null) return bodyProblem;

            if (OrderTargetEndpointSupport.CheckIds(body!.FeatureIds, "featureIds") is { } idsProblem) return idsProblem;
            if (string.IsNullOrWhiteSpace(body.ExcludingOrderId))
                return WorkflowProblemResults.Validation("excludingOrderId is required.");
            if (OrderTargetEndpointSupport.CheckPaging(body.Page, body.PageSize) is { } pagingProblem) return pagingProblem;

            // The lookup searches the organization that owns the excluded order, so the caller
            // must be allowed on that order. An order that does not exist yet is fine: the store
            // then searches the caller's own organization only.
            var excludingOrderId = await UsagesScopeAsync(body.ExcludingOrderId, orders, rules, principalFactory, ct).ConfigureAwait(false);
            if (excludingOrderId.Problem is not null) return excludingOrderId.Problem;

            var usages = await targets.FindOpenUsagesAsync(body.FeatureIds!, excludingOrderId.OrderId!, ct).ConfigureAwait(false);
            var ordered = usages
                .OrderBy(u => u.FeatureId, StringComparer.Ordinal)
                .ThenBy(u => u.OrderId, StringComparer.Ordinal)
                .ToList();
            var items = ordered.Skip((body.Page - 1) * body.PageSize).Take(body.PageSize).ToList();

            return Results.Json(new PagedResponse<OrderTargetUsage>(items, body.Page, body.PageSize, ordered.Count), opts);
        }).RequireAuthorization(ReadPolicy);
    }

    /// <summary>
    /// The order id to scope a usages lookup by. An excluded order the caller is outside the
    /// organization of (XD01-332) is treated exactly like one that does not exist: the lookup
    /// then runs under an id no order has, so the store searches the caller's own organization
    /// only and the response neither reads the other organization's targets nor confirms that
    /// the id names an order there. A caller inside the gate whom the rules decline keeps its 403.
    /// </summary>
    private static async Task<(string? OrderId, IResult? Problem)> UsagesScopeAsync(
        string excludingOrderId, IServiceOrderRepository orders, ServiceOrderRules rules,
        ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(excludingOrderId, ct).ConfigureAwait(false);
        if (order is null) return (excludingOrderId, null);

        var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
        if (!rules.PassesOrganizationGate(principal, order))
            return (Guid.NewGuid().ToString(), null);

        var access = await OrderTargetEndpointSupport.ForReadAsync(excludingOrderId, orders, rules, principalFactory, ct).ConfigureAwait(false);
        return access.Problem is null ? (excludingOrderId, null) : (null, access.Problem);
    }
}
