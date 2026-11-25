using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Hosting;

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
///
/// Every write endpoint on an existing order is gated by <see cref="ServiceOrderRules"/>
/// (XD01-16) — the same deny-overrides engine the Blazor client already evaluates — via
/// <c>OrderAction</c>: dispatch → <c>Dispatch</c>; the action log endpoint → the action
/// the caller is actually logging; create → <c>CanCreate</c>. <c>PUT</c> (a whole-order
/// replace with no single corresponding <c>OrderAction</c>) and <c>DELETE</c> (a hard
/// delete, likewise unnamed in the enum) reuse <c>Annotate</c> and <c>Cancel</c> respectively
/// as the closest existing verb — see the deny-reason "reason" field returned on 403 for
/// exactly which rule declined. Order Type CRUD is unchanged/ungated: that's configuration
/// data, not a per-order action <c>ServiceOrderRules</c> governs.
///
/// <b>Organization gate (XD01-332).</b> <see cref="ServiceOrderRules"/> refuses a caller
/// outside the order's organization before any rule runs; every route here answers that
/// refusal with the same 404 as an unknown order id (see <see cref="ServiceOrderRouteAccess"/>),
/// and keeps 403 for a caller inside the gate whom the rules decline. The read routes, which
/// evaluate no rule, apply the gate themselves: by id and parent through
/// <see cref="ServiceOrderRouteAccess.FindReadableAsync"/>, lists through
/// <see cref="ServiceOrderRouteAccess.KeepReadableAsync"/>. Create stamps the caller's own
/// organization; update never changes it (<see cref="ServiceOrderOrganization"/>).
///
/// Reads additionally call <c>.RequireAuthorization("serviceorders:read")</c> (XD01-127) —
/// a raw <c>AppPermission.Code</c> already seeded and granted to every built-in role
/// (<c>GeoIdentitySeeder</c>), resolved the same way as the Identity endpoints' per-route
/// codes (XD01-15). Writes are deliberately left as-is rather than also gated by a raw
/// permission code: <c>ServiceOrderRules</c> already evaluates the caller's permissions
/// per-order (Creator/Assignee/Role rules, XD01-16), which a blanket
/// <c>"serviceorders:edit"</c> check would not improve — and no such code exists in
/// <c>GeoIdentitySeeder</c>, which seeds <c>create</c>/<c>read</c>/<c>assign</c>/
/// <c>complete</c>/<c>cancel</c> instead.
/// </summary>
public static class ServiceOrdersRestApiExtensions
{
    private static readonly string[] EmptySchemaBodyError = ["Request body must contain a JSON Schema document."];

    private static readonly string[] UndefinedRecipientActionError = ["recipientActions may only name order actions."];

    private const string ReadPolicy = "serviceorders:read";

    public static IEndpointRouteBuilder MapServiceOrdersApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/workflow")
    {
        var opts = GeoJsonSerializer.Options;

        MapServiceOrderReads(routes, prefix, opts);
        MapServiceOrderWrites(routes, prefix, opts);
        MapOrderTypesApi(routes, prefix, opts);

        return routes;
    }

    // ── Service Orders — reads ──────────────────────────────────────────────

    private static void MapServiceOrderReads(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        MapServiceOrderLists(routes, prefix, opts);
        MapServiceOrderFilteredLists(routes, prefix, opts);
        MapServiceOrderById(routes, prefix, opts);
    }

    /// <summary>
    /// Every list is what the store returns for the caller's tenant, narrowed again to the
    /// orders the caller passes the organization gate on (XD01-332, see
    /// <see cref="ServiceOrderRouteAccess.KeepReadableAsync"/>).
    /// </summary>
    private static async Task<IResult> ListAsync(
        Task<IReadOnlyList<IServiceOrder>> query, ServiceOrderRules rules,
        ServerWorkflowPrincipalFactory principalFactory, JsonSerializerOptions opts, CancellationToken ct)
    {
        var orders = await query.ConfigureAwait(false);
        return Results.Json(
            await ServiceOrderRouteAccess.KeepReadableAsync(orders, rules, principalFactory, ct).ConfigureAwait(false), opts);
    }

    /// <summary>The orders of <paramref name="query"/> in <paramref name="status"/>; all of them when no status is given.</summary>
    private static async Task<IReadOnlyList<IServiceOrder>> InStatusAsync(Task<IReadOnlyList<IServiceOrder>> query, string? status)
    {
        var orders = await query.ConfigureAwait(false);
        return string.IsNullOrEmpty(status)
            ? orders
            : [.. orders.Where(o => string.Equals(o.Status, status, StringComparison.Ordinal))];
    }

    /// <summary>
    /// The caller's orders plus, on a host that registers an <see cref="ICrossOrganizationServiceOrderReader"/> (the kernel),
    /// the orders its organization's grants reach (XD01-408, see <see cref="GrantedServiceOrderList"/>), each kept by the
    /// organization gate as the by-id read keeps it. A host without a reader answers exactly as every other list does.
    /// </summary>
    private static async Task<IResult> ListAllAsync(
        IServiceOrderRepository repo, ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory,
        IServiceProvider services, JsonSerializerOptions opts, CancellationToken ct)
    {
        var crossOrganization = services.GetService<ICrossOrganizationServiceOrderReader>();
        if (crossOrganization is null)
            return await ListAsync(repo.GetAllAsync(ct), rules, principalFactory, opts, ct).ConfigureAwait(false);

        var own = await repo.GetAllAsync(ct).ConfigureAwait(false);
        var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
        var orders = await GrantedServiceOrderList.ComposeAsync(own, crossOrganization, rules, principal, ct).ConfigureAwait(false);
        return Results.Json(orders, opts);
    }

    private static void MapServiceOrderLists(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders", (
            IServiceOrderRepository repo, ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory,
            IServiceProvider services, CancellationToken ct) =>
            ListAllAsync(repo, rules, principalFactory, services, opts, ct)).RequireAuthorization(ReadPolicy);

        routes.MapGet($"{prefix}/service-orders/roots", (
            IServiceOrderRepository repo, ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            ListAsync(repo.GetRootsAsync(ct), rules, principalFactory, opts, ct)).RequireAuthorization(ReadPolicy);

        routes.MapGet($"{prefix}/service-orders/{{id}}/children", (
            string id, IServiceOrderRepository repo, ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            ListAsync(repo.GetChildrenAsync(id, ct), rules, principalFactory, opts, ct)).RequireAuthorization(ReadPolicy);
    }

    private static void MapServiceOrderFilteredLists(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders/by-status/{{status}}", (
            string status, IServiceOrderRepository repo, ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            ListAsync(repo.GetByStatusAsync(status, ct), rules, principalFactory, opts, ct)).RequireAuthorization(ReadPolicy);

        routes.MapGet($"{prefix}/service-orders/by-assignee/{{userId}}", (
            string userId, IServiceOrderRepository repo, ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            ListAsync(repo.GetByAssigneeAsync(userId, ct), rules, principalFactory, opts, ct)).RequireAuthorization(ReadPolicy);

        routes.MapGet($"{prefix}/service-orders/by-creator/{{userId}}", (
            string userId, IServiceOrderRepository repo, ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            ListAsync(repo.GetByCreatorAsync(userId, ct), rules, principalFactory, opts, ct)).RequireAuthorization(ReadPolicy);

        // XD01-365: an optional ?status= narrows the type's orders to one state (e.g. the tenant portal's flagged notices).
        routes.MapGet($"{prefix}/service-orders/by-order-type/{{orderTypeId}}", (
            string orderTypeId, string? status, IServiceOrderRepository repo, ServiceOrderRules rules,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            ListAsync(InStatusAsync(repo.GetByOrderTypeAsync(orderTypeId, ct), status), rules, principalFactory, opts, ct))
            .RequireAuthorization(ReadPolicy);

        routes.MapGet($"{prefix}/service-orders/by-date-range", (
            DateTime from, DateTime to, IServiceOrderRepository repo, ServiceOrderRules rules,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            ListAsync(repo.GetByDateRangeAsync(from, to, ct), rules, principalFactory, opts, ct)).RequireAuthorization(ReadPolicy);

        routes.MapGet($"{prefix}/service-orders/dispatched-to/{{targetId}}", (
            string targetId, DispatchTargetType targetType, IServiceOrderRepository repo, ServiceOrderRules rules,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
            ListAsync(repo.GetDispatchedToAsync(targetId, targetType, ct), rules, principalFactory, opts, ct)).RequireAuthorization(ReadPolicy);
    }

    /// <summary>
    /// The two reads that fetch one order past the tenant filter. Both answer 404 for an order
    /// the caller is outside the organization of, exactly as for one that does not exist.
    /// </summary>
    private static void MapServiceOrderById(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/service-orders/{{id}}", async (
            string id, IServiceOrderRepository repo, ServiceOrderRules rules,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var order = await ServiceOrderRouteAccess.FindReadableAsync(id, repo, rules, principalFactory, ct).ConfigureAwait(false);
            return order is null ? Results.NotFound() : Results.Json(order, opts);
        }).RequireAuthorization(ReadPolicy);

        routes.MapGet($"{prefix}/service-orders/{{id}}/parent", async (
            string id, IServiceOrderRepository repo, ServiceOrderRules rules,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var parent = await ServiceOrderRouteAccess
                .KeepIfReadableAsync(await repo.GetParentAsync(id, ct).ConfigureAwait(false), rules, principalFactory, ct)
                .ConfigureAwait(false);
            return parent is null ? Results.NotFound() : Results.Json(parent, opts);
        }).RequireAuthorization(ReadPolicy);
    }

    // ── Service Orders — writes ─────────────────────────────────────────────

    private static void MapServiceOrderWrites(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        MapServiceOrderCreateAndUpdate(routes, prefix, opts);
        MapServiceOrderActionsAndDelete(routes, prefix, opts);
    }

    private static void MapServiceOrderCreateAndUpdate(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        MapServiceOrderCreate(routes, prefix, opts);
        MapServiceOrderUpdate(routes, prefix, opts);
    }

    private static void MapServiceOrderCreate(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/service-orders", async (
            HttpRequest req, IServiceOrderRepository repo, OrderTypeRegistry orderTypeRegistry,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory) =>
        {
            var order = await JsonSerializer.DeserializeAsync<ServiceOrder>(req.Body, opts).ConfigureAwait(false);
            if (order is null) return Results.BadRequest("Invalid service order.");

            // Looks up OrderTypeRegistry (the seeded-defaults + DB-persisted-overlay singleton,
            // AddOrderTypeRegistry()/LoadRegistryFromDbAsync in Program.cs) rather than
            // IOrderTypeRepository (raw DB rows only) — the 3 built-in order types are seeded in
            // code, never written to the database, so checking the repository directly rejected
            // every built-in type as "unknown" even though OrderTypes.All (the client's own view
            // of the same registry) correctly listed them as creatable.
            var orderType = orderTypeRegistry.Find(order.OrderTypeId);
            if (orderType is null) return Results.BadRequest($"Unknown order type '{order.OrderTypeId}'.");

            var principal = await principalFactory.CreateAsync().ConfigureAwait(false);
            if (!rules.CanCreate(principal, orderType))
                return Results.Json(new { reason = "Not authorized to create orders of this type." },
                    statusCode: StatusCodes.Status403Forbidden);

            try
            {
                // XD01-332: an order is created under its creator's own organization — stamped
                // with it when the body states none, refused when the body states another.
                var owned = ServiceOrderOrganization.ForCreation(order, ServiceOrderRouteAccess.OrganizationOf(principal));
                await repo.AddAsync(owned).ConfigureAwait(false);
                return Results.Created(new Uri($"{prefix}/service-orders/{order.Id}", UriKind.Relative), null);
            }
            catch (ServiceOrderOrganizationException ex)
            {
                return Results.Json(new { reason = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ServiceOrderAttributeValidationException ex)
            {
                return Results.BadRequest(new { orderTypeId = ex.OrderTypeId, errors = ex.Errors });
            }
        });
    }

    private static void MapServiceOrderUpdate(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPut($"{prefix}/service-orders/{{id}}", async (
            string id, HttpRequest req, IServiceOrderRepository repo,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory) =>
        {
            var order = await JsonSerializer.DeserializeAsync<ServiceOrder>(req.Body, opts).ConfigureAwait(false);
            if (order is null) return Results.BadRequest("Invalid service order.");
            if (!string.Equals(order.Id, id, StringComparison.Ordinal)) return Results.BadRequest("Route id does not match body id.");

            var existing = await repo.GetByIdAsync(id).ConfigureAwait(false);
            if (existing is null) return Results.NotFound();

            var principal = await principalFactory.CreateAsync().ConfigureAwait(false);
            // XD01-333: a recipient of the order's dispatch replaces it only when the order's type
            // lets it edit the order, and even then never changes what the order is.
            var verdict = rules.EvaluateReplace(principal, existing, order);
            if (!verdict.Allowed) return ServiceOrderRouteAccess.Refusal(verdict);

            try
            {
                // XD01-332: the organization is fixed at creation. A body that states another
                // one is refused here, whatever repository is registered behind the route.
                await repo.UpdateAsync(ServiceOrderOrganization.ForUpdate(existing, order)).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (ServiceOrderOrganizationException ex)
            {
                return Results.BadRequest(new { code = ServiceOrderOrganization.ChangeRefusedCode, reason = ex.Message });
            }
            catch (ServiceOrderAttributeValidationException ex)
            {
                return Results.BadRequest(new { orderTypeId = ex.OrderTypeId, errors = ex.Errors });
            }
            catch (InvalidServiceOrderTransitionException ex)
            {
                return Results.BadRequest(new { from = ex.From, to = ex.To });
            }
            catch (TransitionGuardDeniedException ex)
            {
                return TransitionGuardProblem(ex);
            }
            catch (ServiceOrderConcurrencyException)
            {
                return Results.Conflict();
            }
            catch (TargetLedgerUnavailableException ex)
            {
                // A host whose targets live in the global ledger (XD01-382) releases them in the closing transition;
                // the ledger being down rolls the transition back and is a retryable 503, not a 500.
                return WorkflowProblemResults.FromException(ex, id)!;
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });
    }

    private static void MapServiceOrderActionsAndDelete(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        MapServiceOrderDispatchAndActions(routes, prefix, opts);
        MapServiceOrderDelete(routes, prefix);
    }

    private static void MapServiceOrderDispatchAndActions(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/service-orders/{{id}}/dispatch", async (
            string id, HttpRequest req, IServiceOrderRepository repo,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory) =>
        {
            var dispatch = await JsonSerializer.DeserializeAsync<OrderDispatch>(req.Body, opts).ConfigureAwait(false);
            if (dispatch is null) return Results.BadRequest("Invalid dispatch.");

            var order = await repo.GetByIdAsync(id).ConfigureAwait(false);
            if (order is null) return Results.NotFound();

            var principal = await principalFactory.CreateAsync().ConfigureAwait(false);
            var verdict = rules.Evaluate(principal, OrderAction.Dispatch, order);
            if (!verdict.Allowed) return ServiceOrderRouteAccess.Refusal(verdict);

            try
            {
                await repo.AppendDispatchAsync(id, dispatch).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        MapServiceOrderActions(routes, prefix, opts);
    }

    private static void MapServiceOrderActions(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/service-orders/{{id}}/actions", async (
            string id, HttpRequest req, IServiceOrderRepository repo, OrderTypeRegistry orderTypes,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory,
            IEnumerable<IOrderActionHandler> handlers, CancellationToken ct) =>
        {
            var entry = await JsonSerializer.DeserializeAsync<OrderActionLog>(req.Body, opts, ct).ConfigureAwait(false);
            if (entry is null) return Results.BadRequest("Invalid action.");

            var order = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (order is null) return Results.NotFound();

            var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
            // XD01-336: a recipient takes the actions its order's type lets it take, each only along its own transition.
            var verdict = rules.EvaluateAppend(principal, order, entry);
            if (!verdict.Allowed) return ServiceOrderRouteAccess.Refusal(verdict);

            // XD01-360: an action a module's handler claims is that handler's write (e.g. an approval that commits the
            // occupancy staged on the order's poles, XD01-318); every other action is the default append below.
            var orderType = orderTypes.Find(order.OrderTypeId);
            if (orderType is not null && new OrderActionHandlerRegistry(handlers).Find(orderType, entry) is { } handler)
            {
                var outcome = await handler.HandleAsync(new OrderActionContext(order, orderType, entry, principal), ct).ConfigureAwait(false);
                return OrderActionResults.ToResult(outcome);
            }

            try
            {
                await repo.AppendActionAsync(id, entry, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (InvalidServiceOrderTransitionException ex)
            {
                return Results.BadRequest(new { from = ex.From, to = ex.To });
            }
            catch (TransitionGuardDeniedException ex)
            {
                return TransitionGuardProblem(ex);
            }
            catch (TargetLedgerUnavailableException ex)
            {
                return WorkflowProblemResults.FromException(ex, id)!;
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });
    }

    /// <summary>
    /// A transition an <see cref="ITransitionGuard"/> denied (XD01-362): 409 with the guard's own code, its reason and the
    /// transition, so a client tells it apart from an illegal edge (400) or a changed order (409 without a body).
    /// </summary>
    private static IResult TransitionGuardProblem(TransitionGuardDeniedException denied) =>
        WorkflowProblemResults.Create(
            StatusCodes.Status409Conflict, denied.Verdict.Code ?? "workflow.transition-denied",
            "The transition was denied.", denied.Verdict.Reason, ("from", denied.From), ("to", denied.To));

    private static void MapServiceOrderDelete(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapDelete($"{prefix}/service-orders/{{id}}", async (
            string id, IServiceOrderRepository repo,
            ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory) =>
        {
            var order = await repo.GetByIdAsync(id).ConfigureAwait(false);
            if (order is null) return Results.NotFound();

            var principal = await principalFactory.CreateAsync().ConfigureAwait(false);
            var verdict = rules.Evaluate(principal, OrderAction.Cancel, order);
            if (!verdict.Allowed) return ServiceOrderRouteAccess.Refusal(verdict);

            await repo.DeleteAsync(id).ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    // ── Order Types ──────────────────────────────────────────────────────────

    private static void MapOrderTypesApi(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        // XD01-378: the clients register no vertical's order types of their own; they read every
        // type here, the code-registered ones (OrderTypeRegistry) included.
        routes.MapGet($"{prefix}/order-types", async (IOrderTypeRepository repo, OrderTypeRegistry registry) =>
            Results.Json(await AllOrderTypesAsync(repo, registry).ConfigureAwait(false), opts));

        routes.MapGet($"{prefix}/order-types/{{id}}", async (string id, IOrderTypeRepository repo, OrderTypeRegistry registry) =>
        {
            var orderType = await repo.GetByIdAsync(id).ConfigureAwait(false) ?? registry.Find(id);
            return orderType is null ? Results.NotFound() : Results.Json(orderType, opts);
        });

        routes.MapPost($"{prefix}/order-types", async (HttpRequest req, IOrderTypeRepository repo) =>
        {
            var (orderType, unreadable) = await ReadOrderTypeAsync(req, opts).ConfigureAwait(false);
            if (orderType is null) return unreadable!;
            if (OrderTypeProblem(orderType) is { } problem) return problem;
            await repo.AddAsync(orderType).ConfigureAwait(false);
            await repo.SaveChangesAsync().ConfigureAwait(false);
            return Results.Created(new Uri($"{prefix}/order-types/{orderType.Id}", UriKind.Relative), null);
        });

        routes.MapPut($"{prefix}/order-types/{{id}}", async (string id, HttpRequest req, IOrderTypeRepository repo) =>
        {
            var (orderType, unreadable) = await ReadOrderTypeAsync(req, opts).ConfigureAwait(false);
            if (orderType is null) return unreadable!;
            if (!string.Equals(orderType.Id, id, StringComparison.Ordinal)) return Results.BadRequest("Route id does not match body id.");
            if (OrderTypeProblem(orderType) is { } problem) return problem;
            if (await repo.GetByIdAsync(id).ConfigureAwait(false) is null) return Results.NotFound();

            await repo.UpdateAsync(orderType).ConfigureAwait(false);
            await repo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        });

        routes.MapDelete($"{prefix}/order-types/{{id}}", async (string id, IOrderTypeRepository repo) =>
        {
            await repo.DeleteAsync(id).ConfigureAwait(false);
            await repo.SaveChangesAsync().ConfigureAwait(false);
            return Results.NoContent();
        });

        // Pre-validates a candidate OrderType.AttributesSchemaJson document via the same
        // JsonSchema.Net machinery ServiceOrderAttributeValidator already uses to validate
        // order attributes against a stored schema (XD01-205) — lets a client admin check
        // their own dialect form (e.g. Coelba's DIS-NOR-068) before it's ever persisted,
        // with zero code deploy.
        routes.MapPost($"{prefix}/order-types/validate-schema", async (HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            var schemaJson = await reader.ReadToEndAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(schemaJson))
                return Results.BadRequest(new { valid = false, errors = EmptySchemaBodyError });

            var errors = ServiceOrderAttributeValidator.ValidateSchemaDocument(schemaJson);
            return errors.Count == 0
                ? Results.Ok(new { valid = true })
                : Results.BadRequest(new { valid = false, errors });
        });
    }

    /// <summary>
    /// The code-registered order types of <paramref name="registry"/> merged with the persisted ones
    /// of <paramref name="repository"/>: a persisted type replaces the registered one with the same id
    /// (compared as the registry compares ids), ordered by id (ordinal).
    /// </summary>
    private static async Task<IReadOnlyList<OrderType>> AllOrderTypesAsync(IOrderTypeRepository repository, OrderTypeRegistry registry)
    {
        var merged = registry.All.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var persisted in await repository.GetAllAsync().ConfigureAwait(false))
            merged[persisted.Id] = persisted;

        return [.. merged.Values.OrderBy(t => t.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Rejects an order type whose <see cref="OrderType.RecipientAccess"/> is not one of the
    /// enumeration's values (XD01-333), whose <see cref="OrderType.RecipientActions"/> name an
    /// action outside <see cref="OrderAction"/> (XD01-336), or whose <see cref="OrderType.TargetPolicy"/> breaks its
    /// own invariants, before it is stored: <see cref="OrderTypeRegistry"/> validates the same
    /// policy when it loads order types at startup, so a stored invalid policy would stop the host
    /// from starting. Returns null when the type can be stored.
    /// </summary>
    /// <summary>The order type in the request body, or the 400 that answers a body that is not one.</summary>
    private static async Task<(OrderType? OrderType, IResult? Problem)> ReadOrderTypeAsync(HttpRequest req, JsonSerializerOptions opts)
    {
        try
        {
            var orderType = await JsonSerializer.DeserializeAsync<OrderType>(req.Body, opts).ConfigureAwait(false);
            return orderType is null ? (null, Results.BadRequest("Invalid order type.")) : (orderType, null);
        }
        catch (JsonException ex) when (ex.Path?.StartsWith("$.recipientActions", StringComparison.OrdinalIgnoreCase) == true)
        {
            // XD01-366: an order action that is neither a code nor a historical number is refused while reading
            // (OrderActionJsonConverter) — answered as the check below answered an undefined enumeration value.
            return (null, Results.BadRequest(new { errors = UndefinedRecipientActionError }));
        }
    }

    private static IResult? OrderTypeProblem(OrderType orderType)
    {
        if (!Enum.IsDefined(orderType.RecipientAccess))
        {
            return Results.BadRequest(new
            {
                orderTypeId = orderType.Id,
                errors = new[] { $"recipientAccess must be one of {string.Join(", ", Enum.GetValues<OrderRecipientAccess>().Select(a => $"{(int)a} ({a})"))}." },
            });
        }

        if (orderType.RecipientActions.Any(action => action.IsEmpty))
        {
            return Results.BadRequest(new
            {
                orderTypeId = orderType.Id,
                errors = UndefinedRecipientActionError,
            });
        }

        try
        {
            orderType.TargetPolicy?.Validate(orderType.Id);
            return null;
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { orderTypeId = orderType.Id, errors = new[] { ex.Message } });
        }
    }
}
