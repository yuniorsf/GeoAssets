using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server;

/// <summary>
/// Maps Service Order + Order Type REST API endpoints onto any <see cref="IEndpointRouteBuilder"/>,
/// backed by whatever <see cref="IServiceOrderRepository"/>/<see cref="IOrderTypeRepository"/> is
/// registered in DI (see <c>AddWorkflowPersistence</c>).
///
/// Mounted at its own prefix (default <c>/api/workflow</c>), separate from
/// <c>GeoAssetsRestApiExtensions.MapGeoAssetsApi</c>'s <c>/api/geoassets</c> — Service Orders are a
/// distinct domain (Workflow module) from spatial features, and both are served from the same
/// origin, so a distinct path prefix costs nothing CORS-wise.
///
/// Domain exceptions from <c>ValidatingServiceOrderRepository</c>/<c>EFServiceOrderRepository</c>
/// are translated to HTTP status codes so <c>RestServiceOrderRepository</c> (the client) can
/// re-throw the same exception types regardless of backend, honoring
/// <see cref="IServiceOrderWriter"/>'s documented contract.
/// </summary>
public static class ServiceOrdersRestApiExtensions
{
    public static IEndpointRouteBuilder MapServiceOrdersApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/workflow")
    {
        var opts = GeoJsonSerializer.GetOptions();

        // ── Service Orders — reads ──────────────────────────────────────────────

        routes.MapGet($"{prefix}/service-orders", async (IServiceOrderRepository repo) =>
            Results.Json(await repo.GetAllAsync(), opts));

        routes.MapGet($"{prefix}/service-orders/roots", async (IServiceOrderRepository repo) =>
            Results.Json(await repo.GetRootsAsync(), opts));

        routes.MapGet($"{prefix}/service-orders/by-status/{{status}}", async (string status, IServiceOrderRepository repo) =>
            Results.Json(await repo.GetByStatusAsync(status), opts));

        routes.MapGet($"{prefix}/service-orders/by-assignee/{{userId}}", async (string userId, IServiceOrderRepository repo) =>
            Results.Json(await repo.GetByAssigneeAsync(userId), opts));

        routes.MapGet($"{prefix}/service-orders/by-creator/{{userId}}", async (string userId, IServiceOrderRepository repo) =>
            Results.Json(await repo.GetByCreatorAsync(userId), opts));

        routes.MapGet($"{prefix}/service-orders/by-order-type/{{orderTypeId}}", async (string orderTypeId, IServiceOrderRepository repo) =>
            Results.Json(await repo.GetByOrderTypeAsync(orderTypeId), opts));

        routes.MapGet($"{prefix}/service-orders/by-date-range", async (DateTime from, DateTime to, IServiceOrderRepository repo) =>
            Results.Json(await repo.GetByDateRangeAsync(from, to), opts));

        routes.MapGet($"{prefix}/service-orders/dispatched-to/{{targetId}}",
            async (string targetId, DispatchTargetType targetType, IServiceOrderRepository repo) =>
                Results.Json(await repo.GetDispatchedToAsync(targetId, targetType), opts));

        routes.MapGet($"{prefix}/service-orders/{{id}}", async (string id, IServiceOrderRepository repo) =>
        {
            var order = await repo.GetByIdAsync(id);
            return order is null ? Results.NotFound() : Results.Json(order, opts);
        });

        routes.MapGet($"{prefix}/service-orders/{{id}}/children", async (string id, IServiceOrderRepository repo) =>
            Results.Json(await repo.GetChildrenAsync(id), opts));

        routes.MapGet($"{prefix}/service-orders/{{id}}/parent", async (string id, IServiceOrderRepository repo) =>
        {
            var parent = await repo.GetParentAsync(id);
            return parent is null ? Results.NotFound() : Results.Json(parent, opts);
        });

        // ── Service Orders — writes ─────────────────────────────────────────────

        routes.MapPost($"{prefix}/service-orders", async (HttpRequest req, IServiceOrderRepository repo) =>
        {
            var order = await JsonSerializer.DeserializeAsync<ServiceOrder>(req.Body, opts);
            if (order is null) return Results.BadRequest("Invalid service order.");

            try
            {
                await repo.AddAsync(order);
                return Results.Created($"{prefix}/service-orders/{order.Id}", null);
            }
            catch (ServiceOrderAttributeValidationException ex)
            {
                return Results.BadRequest(new { orderTypeId = ex.OrderTypeId, errors = ex.Errors });
            }
        });

        routes.MapPut($"{prefix}/service-orders/{{id}}", async (string id, HttpRequest req, IServiceOrderRepository repo) =>
        {
            var order = await JsonSerializer.DeserializeAsync<ServiceOrder>(req.Body, opts);
            if (order is null) return Results.BadRequest("Invalid service order.");
            if (order.Id != id) return Results.BadRequest("Route id does not match body id.");

            try
            {
                await repo.UpdateAsync(order);
                return Results.NoContent();
            }
            catch (ServiceOrderAttributeValidationException ex)
            {
                return Results.BadRequest(new { orderTypeId = ex.OrderTypeId, errors = ex.Errors });
            }
            catch (InvalidServiceOrderTransitionException ex)
            {
                return Results.BadRequest(new { from = ex.From, to = ex.To });
            }
            catch (ServiceOrderConcurrencyException)
            {
                return Results.Conflict();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        routes.MapPost($"{prefix}/service-orders/{{id}}/dispatch", async (string id, HttpRequest req, IServiceOrderRepository repo) =>
        {
            var dispatch = await JsonSerializer.DeserializeAsync<OrderDispatch>(req.Body, opts);
            if (dispatch is null) return Results.BadRequest("Invalid dispatch.");

            try
            {
                await repo.AppendDispatchAsync(id, dispatch);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        routes.MapPost($"{prefix}/service-orders/{{id}}/actions", async (string id, HttpRequest req, IServiceOrderRepository repo) =>
        {
            var entry = await JsonSerializer.DeserializeAsync<OrderActionLog>(req.Body, opts);
            if (entry is null) return Results.BadRequest("Invalid action.");

            try
            {
                await repo.AppendActionAsync(id, entry);
                return Results.NoContent();
            }
            catch (InvalidServiceOrderTransitionException ex)
            {
                return Results.BadRequest(new { from = ex.From, to = ex.To });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        routes.MapDelete($"{prefix}/service-orders/{{id}}", async (string id, IServiceOrderRepository repo) =>
        {
            await repo.DeleteAsync(id);
            return Results.NoContent();
        });

        // ── Order Types ──────────────────────────────────────────────────────────

        routes.MapGet($"{prefix}/order-types", async (IOrderTypeRepository repo) =>
            Results.Json(await repo.GetAllAsync(), opts));

        routes.MapGet($"{prefix}/order-types/{{id}}", async (string id, IOrderTypeRepository repo) =>
        {
            var orderType = await repo.GetByIdAsync(id);
            return orderType is null ? Results.NotFound() : Results.Json(orderType, opts);
        });

        routes.MapPost($"{prefix}/order-types", async (HttpRequest req, IOrderTypeRepository repo) =>
        {
            var orderType = await JsonSerializer.DeserializeAsync<OrderType>(req.Body, opts);
            if (orderType is null) return Results.BadRequest("Invalid order type.");
            await repo.AddAsync(orderType);
            await repo.SaveChangesAsync();
            return Results.Created($"{prefix}/order-types/{orderType.Id}", null);
        });

        routes.MapPut($"{prefix}/order-types/{{id}}", async (string id, HttpRequest req, IOrderTypeRepository repo) =>
        {
            var orderType = await JsonSerializer.DeserializeAsync<OrderType>(req.Body, opts);
            if (orderType is null) return Results.BadRequest("Invalid order type.");
            if (orderType.Id != id) return Results.BadRequest("Route id does not match body id.");
            if (await repo.GetByIdAsync(id) is null) return Results.NotFound();

            await repo.UpdateAsync(orderType);
            await repo.SaveChangesAsync();
            return Results.NoContent();
        });

        routes.MapDelete($"{prefix}/order-types/{{id}}", async (string id, IOrderTypeRepository repo) =>
        {
            await repo.DeleteAsync(id);
            await repo.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }
}
