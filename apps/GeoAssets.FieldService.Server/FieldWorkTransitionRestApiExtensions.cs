using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the crew-facing field-work transition endpoint (XD01-295) — the missing host-level
/// surface for <c>FieldWorkOrderTransitioner</c>/<c>CompositeFieldWorkTransitionGuard</c>
/// (XD01-220/221/222/246), which already implement the full guarded state machine (geofence
/// arrival, digital APR safety checklist, before/during/after photo-evidence gating) but had
/// zero host-layer wiring anywhere in the repo before this ticket. This file adds nothing to
/// those guards' own enforcement (already covered end-to-end by
/// <c>FieldWorkOrderTransitionerTests</c>/<c>CompositeFieldWorkTransitionGuardTests</c>/
/// <c>PhotoGatingEndToEndTests</c>/<c>StartExecutionSafetyGateEndToEndTests</c>) — only the thin
/// HTTP façade and its tenant/concurrency/error mapping, same scope as
/// <see cref="FieldWorkInspectionRestApiExtensions"/> for the one edge that already had a host
/// surface.
///
/// Deliberately refuses <see cref="FieldWorkStatus.Inspected"/> and
/// <see cref="FieldWorkStatus.ConflictReview"/> as a requested <c>NewStatus</c> — those are
/// supervisor sign-off (<see cref="FieldWorkInspectionRestApiExtensions"/>, which additionally
/// posts US ledger lines and stages the Core GIS outbox event atomically) and reviewer
/// adjudication respectively, neither of which this generic crew-side endpoint should be able to
/// produce as a side effect of an ordinary structurally-legal transition.
///
/// Tenant boundary: same "cross-tenant id looks like 404, not 403" convention as
/// <see cref="FieldWorkInspectionRestApiExtensions"/>/<see cref="FieldServiceSyncRestApiExtensions"/>.
///
/// Known adjacent gap, deliberately not fixed here: <see cref="FieldServiceSyncRestApiExtensions"/>'s
/// generic mobile-sync push endpoint can still set <c>IServiceOrder.Status</c> directly for any
/// structurally-legal edge (its underlying <c>ValidatingServiceOrderRepository.UpdateAsync</c>
/// only checks <c>ServiceOrderTransitions.IsValid</c>, never <see cref="IFieldWorkTransitionGuard"/>)
/// — a client could still bypass the geofence/safety/photo gates via that route today. This
/// ticket's job was adding the missing guarded surface, not auditing or hardening the
/// pre-existing unguarded one; flagging for a follow-up ticket rather than silently changing
/// Push's behavior, which other order types may rely on.
/// </summary>
internal static class FieldWorkTransitionRestApiExtensions
{
    public static IEndpointRouteBuilder MapFieldWorkTransitionApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/orders")
    {
        routes.MapPost($"{prefix}/{{serviceOrderId}}/field-work-transition", HandleAsync)
        .WithName("AttemptFieldWorkTransition")
        .WithSummary("Attempts to advance a field-work order to a new status, enforcing the geofence/safety-checklist/photo-evidence gates for the three edges that carry one.")
        .Produces<FieldWorkTransitionResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status500InternalServerError);

        return routes;
    }

    private static async Task<IResult> HandleAsync(
        string serviceOrderId,
        FieldWorkTransitionRequest request,
        IServiceOrderRepository serviceOrders,
        ITenantContext tenantContext,
        OrderTypeRegistry orderTypeRegistry,
        IFieldWorkTransitionGuard guard,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (string.Equals(request.NewStatus, FieldWorkStatus.Inspected, StringComparison.Ordinal)
            || string.Equals(request.NewStatus, FieldWorkStatus.ConflictReview, StringComparison.Ordinal))
        {
            return Results.BadRequest(
                $"'{request.NewStatus}' is not attemptable through this endpoint — use the supervisor sign-off endpoint (/api/field-service/orders/{{id}}/inspect) or reviewer adjudication instead.");
        }

        var existing = await serviceOrders.GetByIdAsync(serviceOrderId, ct).ConfigureAwait(false);
        if (existing is null || existing.OrganizationId != tenantContext.CurrentOrganizationId)
            return Results.NotFound();

        if (existing is not ServiceOrder order)
            return Results.Problem($"Expected a {nameof(ServiceOrder)} instance, got {existing.GetType().Name}.", statusCode: StatusCodes.Status500InternalServerError);

        var fieldWorkOrderType = orderTypeRegistry.Find(order.OrderTypeId);
        if (fieldWorkOrderType is null)
            return Results.BadRequest($"OrderType '{order.OrderTypeId}' is not registered.");

        if (request.Attributes is { Count: > 0 })
        {
            foreach (var (key, value) in request.Attributes)
                order.Attributes[key] = value;
        }

        GeoPoint? devicePoint = request.DeviceLongitude is { } lon && request.DeviceLatitude is { } lat
            ? new GeoPoint(lon, lat)
            : null;

        try
        {
            var transitioned = await FieldWorkOrderTransitioner.TransitionAsync(
                order, request.NewStatus, timeProvider, fieldWorkOrderType, guard, devicePoint, ct).ConfigureAwait(false);

            await serviceOrders.UpdateAsync(CopyWithRowVersion(transitioned, request.RowVersion), ct).ConfigureAwait(false);

            return Results.Ok(FieldWorkTransitionResponse.FromDomain(transitioned));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (InvalidServiceOrderTransitionException ex)
        {
            return Results.BadRequest(
                $"Transition from '{ex.From}' to '{ex.To}' was rejected — either not a structurally legal edge, or a required gate (geofence arrival, safety checklist, or photo evidence) was not satisfied.");
        }
        catch (ServiceOrderConcurrencyException)
        {
            return Results.Conflict("This order was modified by another writer since it was last read. Reload and retry.");
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static ServiceOrder CopyWithRowVersion(ServiceOrder source, byte[] rowVersion) => new()
    {
        Id = source.Id,
        RowVersion = rowVersion,
        Title = source.Title,
        Description = source.Description,
        OrderTypeId = source.OrderTypeId,
        Status = source.Status,
        Priority = source.Priority,
        CreatedBy = source.CreatedBy,
        AssignedTo = source.AssignedTo,
        OrganizationId = source.OrganizationId,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        ScheduledAt = source.ScheduledAt,
        CompletedAt = source.CompletedAt,
        Attributes = source.Attributes,
        Features = source.Features,
        SelectionSpec = source.SelectionSpec,
        FeatureIds = source.FeatureIds,
        ParentOrderId = source.ParentOrderId,
    };
}
