using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authentication;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the supervisor sign-off endpoint (XD01-248) onto any <see cref="IEndpointRouteBuilder"/>
/// — the missing host-level surface for <see cref="InspectionCommitService"/> (XD01-228), which
/// already implements the full single-transaction commit (status advance + ledger postings +
/// outbox staging, optimistic-concurrency-checked) but had no REST endpoint actually calling it
/// from <c>GeoAssets.FieldService.Server</c>'s <c>Program.cs</c> before this ticket. This file
/// adds nothing to that service's own ACID guarantee (already covered end-to-end by
/// <c>InspectionCommitServiceTests</c>) — only the thin HTTP façade and its tenant/error mapping.
///
/// Tenant boundary: same "cross-tenant id looks like 404, not 403" convention as
/// <see cref="ContractorUsLedgerRestApiExtensions"/> and <see cref="FieldServiceSyncRestApiExtensions"/>
/// — this host's <c>EFServiceOrderRepository.GetByIdAsync</c> deliberately bypasses the global
/// tenant query filter (XD01-201's cross-org-grant exception), so the organization check below is
/// explicit rather than relying on the filter alone.
///
/// <see cref="InspectServiceOrderRequest.CatalogInstanceId"/> is caller-supplied (which Core GIS
/// catalog instance <see cref="InspectionCommitService"/> stages the outbox mutation event
/// against) rather than configuration, matching how <see cref="GeoCoreBridgeEventPublisher"/>'s
/// own caller already treats it as a per-request value. The performing user comes from the
/// authenticated principal (<see cref="ICurrentUserAccessor"/>), never the request body — an
/// inspection sign-off is an audit-critical action, not something a caller should be able to
/// attribute to someone else.
/// </summary>
internal static class FieldWorkInspectionRestApiExtensions
{
    public static IEndpointRouteBuilder MapFieldWorkInspectionApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/field-service/orders")
    {
        routes.MapPost($"{prefix}/{{serviceOrderId}}/inspect", async (
            string serviceOrderId,
            InspectServiceOrderRequest request,
            IServiceOrderRepository serviceOrders,
            ITenantContext tenantContext,
            ICurrentUserAccessor currentUserAccessor,
            InspectionCommitService commitService,
            CancellationToken ct) =>
        {
            var existing = await serviceOrders.GetByIdAsync(serviceOrderId, ct).ConfigureAwait(false);
            if (existing is null || existing.OrganizationId != tenantContext.CurrentOrganizationId)
                return Results.NotFound();

            var currentUser = await currentUserAccessor.GetCurrentUserAsync(ct).ConfigureAwait(false);
            var performedByUserId = currentUser?.ExternalObjectId;
            if (string.IsNullOrEmpty(performedByUserId))
                return Results.Unauthorized();

            try
            {
                var result = await commitService.CommitAsync(
                    serviceOrderId, request.RowVersion, performedByUserId, request.CatalogInstanceId, ct).ConfigureAwait(false);

                return Results.Ok(InspectionCommitResponse.FromDomain(result));
            }
            catch (ServiceOrderConcurrencyException)
            {
                return Results.Conflict("This order was modified by another writer since it was last read. Reload and retry.");
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidServiceOrderTransitionException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (UsCatalogItemNotFoundException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("InspectServiceOrder")
        .WithSummary("Supervisor sign-off: atomically advances a field-work order from Completed to Inspected, posts its US ledger lines, and stages the Core GIS mutation outbox event.")
        .Produces<InspectionCommitResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return routes;
    }
}
