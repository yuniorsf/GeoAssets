using System.Text.Json;
using GeoAssets.Server.Targets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Server;

/// <content>Selection preview (R5), apply (R6) and the map-gesture resolve route.</content>
public static partial class ServiceOrderTargetsRestApiExtensions
{
    /// <summary>The asset-read permission of <c>GeoAssetsRestApiExtensions</c>: resolve returns feature ids, nothing about orders.</summary>
    private const string FeatureReadPolicy = "features:read";

    private static void MapSelectionPreview(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/service-orders/{{id}}/targets/selection/preview", async (
            string id, HttpRequest request, IServiceOrderRepository orders, OrderTypeRegistry orderTypes,
            BoundedFeatureSelector selector, IOrderTargetSelectionService selection,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var (selectionRequest, order, problem) = await ReadSelectionAsync(id, request, orders, opts, ct).ConfigureAwait(false);
            if (problem is not null) return problem;

            try
            {
                var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
                var preview = await selection.PreviewAsync(selectionRequest!, principal, ct).ConfigureAwait(false);
                return Results.Json(preview, opts);
            }
            catch (Exception ex)
            {
                if (SelectionProblem(ex, order!, selectionRequest!.StrategyId, orderTypes, selector) is { } failure) return failure;
                throw;
            }
        }).RequireAuthorization(ReadPolicy);
    }

    private static void MapSelectionApply(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/service-orders/{{id}}/targets/selection/apply", async (
            string id, HttpRequest request, IServiceOrderRepository orders, OrderTypeRegistry orderTypes,
            BoundedFeatureSelector selector, IOrderTargetSelectionService selection,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var (selectionRequest, order, problem) = await ReadSelectionAsync(id, request, orders, opts, ct).ConfigureAwait(false);
            if (problem is not null) return problem;

            try
            {
                var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
                var result = await selection.ApplyAsync(selectionRequest!, principal, ct).ConfigureAwait(false);
                return Results.Json(result, opts);
            }
            catch (Exception ex)
            {
                if (SelectionProblem(ex, order!, selectionRequest!.StrategyId, orderTypes, selector) is { } failure) return failure;
                throw;
            }
        });
    }

    /// <summary>
    /// Reads and bounds a selection body, then loads the order — so that an unknown order is a
    /// 404 before anything else about the request is judged, and a later
    /// <see cref="KeyNotFoundException"/> can only mean something else.
    /// </summary>
    private static async Task<(TargetSelectionRequest? Request, IServiceOrder? Order, IResult? Problem)> ReadSelectionAsync(
        string orderId, HttpRequest request, IServiceOrderRepository orders, JsonSerializerOptions opts, CancellationToken ct)
    {
        var (body, _, bodyProblem) = await OrderTargetEndpointSupport
            .ReadBodyAsync<TargetSelectionRequestBody>(request, opts, ct).ConfigureAwait(false);
        if (bodyProblem is not null) return (null, null, bodyProblem);

        if (string.IsNullOrWhiteSpace(body!.StrategyId))
            return (null, null, WorkflowProblemResults.Validation("strategyId is required."));
        if (OrderTargetEndpointSupport.CheckPaging(body.Page, body.PageSize) is { } pagingProblem)
            return (null, null, pagingProblem);

        var order = await orders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        if (order is null) return (null, null, WorkflowProblemResults.OrderNotFound(orderId));

        var parameters = body.Parameters ?? new Dictionary<string, object>(StringComparer.Ordinal);
        return (
            new TargetSelectionRequest(orderId, body.StrategyId, parameters, body.Page, body.PageSize, body.AcceptTruncation),
            order,
            null);
    }

    /// <summary>
    /// The problem for a failed selection, or null when the failure is not the caller's to fix
    /// (and must surface as a server error). <see cref="KeyNotFoundException"/> is the one type
    /// that needs context: the order is known to exist (see <see cref="ReadSelectionAsync"/>),
    /// so it means an unregistered order type, an unknown strategy, or — what strategies throw
    /// for a required parameter that is absent — invalid parameters.
    /// </summary>
    private static IResult? SelectionProblem(
        Exception exception, IServiceOrder order, string strategyId, OrderTypeRegistry orderTypes, BoundedFeatureSelector selector)
    {
        var orderType = orderTypes.Find(order.OrderTypeId);

        return exception switch
        {
            KeyNotFoundException when orderType is null => WorkflowProblemResults.Create(
                StatusCodes.Status404NotFound, WorkflowProblemCodes.OrderTypeNotFound, "The order's type is not registered.",
                null, ("orderTypeId", order.OrderTypeId)),
            KeyNotFoundException when !selector.HasStrategy(strategyId) => WorkflowProblemResults.UnknownStrategy(strategyId),
            KeyNotFoundException => WorkflowProblemResults.InvalidSelectionParameters("A required selection parameter is missing."),
            InvalidOperationException when orderType is { TargetPolicy: null }
                && WorkflowProblemResults.FromException(exception, order.Id) is null => WorkflowProblemResults.Create(
                    StatusCodes.Status422UnprocessableEntity, WorkflowProblemCodes.NoTargetPolicy,
                    "Orders of this type hold no targets.", exception.Message, ("orderTypeId", order.OrderTypeId)),
            _ => WorkflowProblemResults.FromException(exception, order.Id),
        };
    }

    private static void MapSelectionResolve(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/selection/resolve", async (
            HttpRequest request, BoundedFeatureSelector selector, OrderTargetSelectionOptions options,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var (body, _, bodyProblem) = await OrderTargetEndpointSupport
                .ReadBodyAsync<ResolveSelectionRequest>(request, opts, ct).ConfigureAwait(false);
            if (bodyProblem is not null) return bodyProblem;

            if (string.IsNullOrWhiteSpace(body!.StrategyId))
                return WorkflowProblemResults.Validation("strategyId is required.");
            if (!IsMapGestureStrategy(body.StrategyId))
                return WorkflowProblemResults.UnknownStrategy(body.StrategyId);

            var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
            var bounded = new BoundedFeatureSelectionRequest(
                body.StrategyId,
                body.Parameters ?? new Dictionary<string, object>(StringComparer.Ordinal),
                OrderTargetEndpointSupport.OrganizationOf(principal),
                Math.Min(options.MaxResolved, options.MaxMatched));

            try
            {
                var selected = await selector.SelectAsync(bounded, ct).ConfigureAwait(false);
                var ids = selected.Features.Select(f => f.Id).ToList();
                return Results.Json(new ResolveSelectionResponse(ids, ids.Count), opts);
            }
            catch (KeyNotFoundException)
            {
                return WorkflowProblemResults.InvalidSelectionParameters("A required selection parameter is missing.");
            }
            catch (Exception ex)
            {
                if (WorkflowProblemResults.FromException(ex, string.Empty) is { } failure) return failure;
                throw;
            }
        }).RequireAuthorization(FeatureReadPolicy);
    }

    /// <summary>
    /// Resolve runs outside any order, so only the two strategies that go through the
    /// organization-scoped spatial query are offered; a box gesture is a rectangular fence.
    /// </summary>
    private static bool IsMapGestureStrategy(string strategyId) =>
        string.Equals(strategyId, PolygonFenceSelectionStrategy.Id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(strategyId, IdentifierListSelectionStrategy.Id, StringComparison.OrdinalIgnoreCase);
}
