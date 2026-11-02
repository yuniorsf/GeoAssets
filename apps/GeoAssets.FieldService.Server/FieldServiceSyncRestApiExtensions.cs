using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the mobile offline-sync endpoints (XD01-208) onto any <see cref="IEndpointRouteBuilder"/>
/// — the entry point .NET MAUI field clients hit to pull/push <see cref="IServiceOrder"/> deltas,
/// ahead of the deterministic 3-way conflict reconciliation engine tracked separately (that work
/// replaces the simple "last write wins, surface a conflict" behavior here, not add to it).
///
/// Deliberately scoped to orders that already exist server-side: a field crew's mobile client
/// receives its dispatched orders via <see cref="MapPull"/>, works on them offline, then pushes
/// results back via <see cref="MapPush"/> — it never originates a brand-new order type from the
/// field, so this surface doesn't need the order-type-registry/creation-rules machinery
/// <c>ServiceOrdersRestApiExtensions.MapServiceOrderCreate</c> (GeoAssets.Server) depends on,
/// which this host doesn't register. Pushing an id with no matching existing order is reported
/// as a per-item failure, not silently created.
///
/// Tenant boundary: <see cref="MapPull"/> relies on <see cref="ServiceOrderDbContext"/>'s global
/// query filter (same as every other read through <see cref="IServiceOrderRepository.GetAllAsync"/>).
/// <see cref="MapPush"/> cannot rely on that alone — <c>EFServiceOrderRepository.GetByIdAsync</c>
/// deliberately calls <c>IgnoreQueryFilters()</c> for ServiceOrders' cross-org-grant exception
/// (XD01-201), which this host has no <c>OrganizationGrants</c>/<c>ServiceOrderRules</c> wiring to
/// evaluate — so it checks <see cref="IOrgOwnedResource.OrganizationId"/> against the caller's own
/// <see cref="ITenantContext.CurrentOrganizationId"/> itself before allowing a write, treating a
/// mismatch identically to "order not found" rather than a distinguishable 403 (same reasoning as
/// <c>ContractorUsLedgerRestApiExtensions</c>, XD01-207).
/// </summary>
internal static class FieldServiceSyncRestApiExtensions
{
    /// <summary>
    /// <see cref="ServiceOrder.Attributes"/> key the last successfully-applied push's
    /// idempotency key is stamped under (XD01-236) — the dedup check's source of truth.
    /// </summary>
    internal const string LastPushIdempotencyKeyAttribute = "lastPushIdempotencyKey";

    public static IEndpointRouteBuilder MapFieldServiceSyncApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/sync")
    {
        var opts = GeoJsonSerializer.Options;

        MapPull(routes, prefix, opts);
        MapPush(routes, prefix, opts);

        return routes;
    }

    private static void MapPull(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders", async (DateTime sinceUtc, IServiceOrderRepository repo, CancellationToken ct) =>
        {
            // GetAllAsync(), not a dedicated indexed query — this is the deliberately simple
            // first-cut sync surface the class doc comment describes; a change-feed query
            // (likely alongside the XD01-223 reconciliation engine, once there's a real access
            // pattern to optimize for) can replace the in-memory filter below without changing
            // this route's contract.
            var orders = await repo.GetAllAsync(ct).ConfigureAwait(false);
            var modified = orders.Where(o => o.CreatedAt >= sinceUtc || o.UpdatedAt >= sinceUtc).ToList();

            return Results.Json(modified, opts);
        })
        .WithName("PullServiceOrderDeltas")
        .WithSummary("Pulls ServiceOrders created or updated since sinceUtc, scoped to the caller's own tenant.");
    }

    private static void MapPush(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/service-orders", async (
            HttpRequest req, IServiceOrderRepository repo, ITenantContext tenantContext, CancellationToken ct) =>
        {
            var incoming = await JsonSerializer.DeserializeAsync<List<PushServiceOrderItem>>(req.Body, opts, ct).ConfigureAwait(false);
            if (incoming is null) return Results.BadRequest("Invalid service order batch.");

            // Sequential, not parallel: multiple items in one batch can legitimately target the
            // same ServiceOrder.Id (a device that queued several edits offline before
            // reconnecting) — each item's own fresh GetByIdAsync must see the previous item's
            // write, not race it.
            var results = new List<PushServiceOrderResult>(incoming.Count);
            foreach (var item in incoming)
            {
                results.Add(await PushOneAsync(item, repo, tenantContext, ct).ConfigureAwait(false));
            }

            return Results.Ok(results);
        })
        .WithName("PushServiceOrderDeltas")
        .WithSummary("Pushes locally-modified ServiceOrders for orders that already exist under the caller's own tenant, deduped by idempotency key.")
        .Produces<List<PushServiceOrderResult>>()
        .Produces(StatusCodes.Status400BadRequest);
    }

    private static async Task<PushServiceOrderResult> PushOneAsync(
        PushServiceOrderItem item, IServiceOrderRepository repo, ITenantContext tenantContext, CancellationToken ct)
    {
        var order = item.Order;
        var existing = await repo.GetByIdAsync(order.Id, ct).ConfigureAwait(false);
        if (existing is null)
            return new PushServiceOrderResult(order.Id, Success: false, Error: "ServiceOrder not found.");

        if (existing.OrganizationId != tenantContext.CurrentOrganizationId)
            return new PushServiceOrderResult(order.Id, Success: false, Error: "ServiceOrder not found.");

        // A reconnect burst may replay the same already-applied item (the client never saw the
        // first attempt's ack) — short-circuit rather than re-run UpdateAsync, which would
        // otherwise either silently double-apply or spuriously conflict against the RowVersion
        // the first successful attempt already advanced.
        if (existing.Attributes.TryGetValue(LastPushIdempotencyKeyAttribute, out var lastKey) &&
            string.Equals(lastKey, item.IdempotencyKey, StringComparison.Ordinal))
        {
            return new PushServiceOrderResult(order.Id, Success: true, Error: null, Duplicate: true);
        }

        order.Attributes[LastPushIdempotencyKeyAttribute] = item.IdempotencyKey;

        try
        {
            await repo.UpdateAsync(order, ct).ConfigureAwait(false);
            return new PushServiceOrderResult(order.Id, Success: true, Error: null);
        }
        catch (ServiceOrderConcurrencyException)
        {
            return new PushServiceOrderResult(order.Id, Success: false,
                Error: "Conflict: this order was modified by another writer since the client's last pull.");
        }
        catch (InvalidServiceOrderTransitionException ex)
        {
            return new PushServiceOrderResult(order.Id, Success: false, Error: ex.Message);
        }
    }
}
