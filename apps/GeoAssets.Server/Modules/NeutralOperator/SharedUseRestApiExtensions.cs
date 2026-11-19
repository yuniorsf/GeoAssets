using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Server.Modules.NeutralOperator.SharedUse;
using GeoAssets.Server.Targets;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// The shared-use dispatch routes (XD01-316), under the same prefix as the other service-order
/// routes. A shared-use order — one whose type declares a fan-out — is dispatched to every
/// occupant organization with an active contract on its poles, one child order each.
///
/// <list type="bullet">
/// <item><c>GET  /service-orders/{id}/shared-use/dispatch-plan</c> — what a dispatch would do
/// now: the plan's counts and one page of its recipients;</item>
/// <item><c>GET  /service-orders/{id}/shared-use/dispatch-plan/skipped-entries</c> — one page of
/// the occupancy entries that get no dispatch, each with its reason;</item>
/// <item><c>GET  /service-orders/{id}/shared-use/dispatch-plan/unnotifiable-poles</c> — one page
/// of the poles with nobody to notify;</item>
/// <item><c>POST /service-orders/{id}/shared-use/dispatch</c> — the dispatch itself. Repeating
/// it is safe: recipients that already have their order are left alone.</item>
/// </list>
///
/// Every list is paged (<c>page</c> from 1, <c>pageSize</c> 1 to 200, default 50). The caller is
/// always the principal of the token. All four ask the rules for <c>Dispatch</c> and are for the
/// order's own organization only: a caller the organization gate refuses gets 404 exactly as for
/// an unknown order, a cross-organization grantee or a recipient gets 403. Failures are
/// <c>application/problem+json</c> with a stable <c>code</c> (see <see cref="SharedUseProblemMapper"/>).
/// </summary>
public static class SharedUseRestApiExtensions
{
    private const string ReadPolicy = "serviceorders:read";

    public static IEndpointRouteBuilder MapSharedUseDispatchApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/workflow")
    {
        ArgumentNullException.ThrowIfNull(routes);

        var opts = GeoJsonSerializer.Options;

        MapPlan(routes, prefix, opts);
        MapPlanLists(routes, prefix, opts);
        MapDispatch(routes, prefix, opts);

        return routes;
    }

    private static void MapPlan(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders/{{id}}/shared-use/dispatch-plan", (
            string id, string? page, string? pageSize, ISharedUseDispatchPlanReader plans,
            ServerWorkflowPrincipalFactory principalFactory, IEnumerable<IProblemDetailsMapper> problemMappers, CancellationToken ct) =>
        {
            if (OrderTargetEndpointSupport.ReadPaging(page, pageSize, out var pageNumber, out var size) is { } pagingProblem)
                return Task.FromResult(pagingProblem);

            return RunAsync(id, principalFactory, problemMappers, async principal =>
                Results.Json(await plans.PreviewAsync(id, pageNumber, size, principal, ct).ConfigureAwait(false), opts), ct);
        }).RequireAuthorization(ReadPolicy);
    }

    private static void MapPlanLists(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders/{{id}}/shared-use/dispatch-plan/skipped-entries", (
            string id, string? page, string? pageSize, ISharedUseDispatchPlanReader plans,
            ServerWorkflowPrincipalFactory principalFactory, IEnumerable<IProblemDetailsMapper> problemMappers, CancellationToken ct) =>
        {
            if (OrderTargetEndpointSupport.ReadPaging(page, pageSize, out var pageNumber, out var size) is { } pagingProblem)
                return Task.FromResult(pagingProblem);

            return RunAsync(id, principalFactory, problemMappers, async principal =>
            {
                var result = await plans.GetSkippedEntriesAsync(id, pageNumber, size, principal, ct).ConfigureAwait(false);
                return Results.Json(new PagedResponse<SharedUseSkippedEntry>(result.Items, pageNumber, size, result.TotalCount), opts);
            }, ct);
        }).RequireAuthorization(ReadPolicy);

        routes.MapGet($"{prefix}/service-orders/{{id}}/shared-use/dispatch-plan/unnotifiable-poles", (
            string id, string? page, string? pageSize, ISharedUseDispatchPlanReader plans,
            ServerWorkflowPrincipalFactory principalFactory, IEnumerable<IProblemDetailsMapper> problemMappers, CancellationToken ct) =>
        {
            if (OrderTargetEndpointSupport.ReadPaging(page, pageSize, out var pageNumber, out var size) is { } pagingProblem)
                return Task.FromResult(pagingProblem);

            return RunAsync(id, principalFactory, problemMappers, async principal =>
            {
                var result = await plans.GetUnnotifiablePolesAsync(id, pageNumber, size, principal, ct).ConfigureAwait(false);
                return Results.Json(new PagedResponse<string>(result.Items, pageNumber, size, result.TotalCount), opts);
            }, ct);
        }).RequireAuthorization(ReadPolicy);
    }

    private static void MapDispatch(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/service-orders/{{id}}/shared-use/dispatch", async (
            string id, HttpRequest request, ISharedUseDispatchService dispatch,
            ServerWorkflowPrincipalFactory principalFactory, IEnumerable<IProblemDetailsMapper> problemMappers, CancellationToken ct) =>
        {
            var (body, _, bodyProblem) = await OrderTargetEndpointSupport
                .ReadBodyAsync<DispatchSharedUseOrderRequest>(request, opts, ct).ConfigureAwait(false);
            if (bodyProblem is not null) return bodyProblem;
            if (body!.RowVersion is not { } rowVersion)
                return WorkflowProblemResults.Validation("rowVersion is required.");

            return await RunAsync(id, principalFactory, problemMappers, async principal =>
                Results.Json(await dispatch.DispatchAsync(id, rowVersion, principal, ct).ConfigureAwait(false), opts), ct)
                .ConfigureAwait(false);
        });
    }

    private static async Task<IResult> RunAsync(
        string orderId, ServerWorkflowPrincipalFactory principalFactory,
        IEnumerable<IProblemDetailsMapper> problemMappers,
        Func<WorkflowPrincipal, Task<IResult>> call, CancellationToken ct)
    {
        var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
        try
        {
            return await call(principal).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && new ProblemDetailsMapperChain(problemMappers).TryMap(ex, new ProblemContext(SharedUseProblemMapper.Scope, orderId)) is { } problem)
        {
            return problem;
        }
    }
}
