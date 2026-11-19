using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// Maps the tenant ISP self-service portal's REST surface (XD01-269) at
/// <c>/api/isp/tenant-portal</c> — attachment-request submission/listing and clandestine-flag
/// disputing, both scoped by the caller's own <c>OrganizationId</c> (<see cref="ITenantContext"/>).
///
/// "Poles flagged for regularization affecting them" and "dispute submissions" both operate on
/// the existing clandestine-notice <c>ServiceOrder</c>s from XD01-263 (<see cref="ClandestineAttachmentOrderTypes"/>).
/// "Attachment requests" are modeled as their own new <c>ServiceOrder</c> type
/// (<see cref="TenantAttachmentRequestOrderTypes"/>) rather than a bespoke aggregate, for the
/// same reason every other order type in this epic is: optimistic concurrency, an audit trail,
/// and real org-scoped isolation "for free."
///
/// <para><b>One decision point (XD01-364, ADR-001 smell H).</b> Every route here decides with <c>ServiceOrderRules</c>,
/// exactly as <c>/api/workflow</c> does: submitting checks the type's creation policies
/// (<see cref="ServiceOrderCreateRequirement"/>); reading keeps only the orders whose organization gate the caller passes
/// (another organization's order is a 404, as an unknown id is); disputing evaluates the <c>Dispute</c> entry
/// (<see cref="ServiceOrderActionRequirement"/>) and is then performed by the module's order action handler — the same
/// one <c>POST /service-orders/{id}/actions</c> reaches. The route permissions stay as the first, coarse check.</para>
/// </summary>
public static class IspTenantPortalRestApiExtensions
{
    private const string ReadPermission = "serviceorders:read";
    private const string WritePermission = "serviceorders:create";

    public static IEndpointRouteBuilder MapIspTenantPortalApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/isp/tenant-portal")
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        MapAttachmentRequests(routes, prefix, opts);
        MapFlaggedNotices(routes, prefix, opts);

        return routes;
    }

    private static void MapAttachmentRequests(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/attachment-requests", async (
            SubmitAttachmentRequestRequest body,
            IServiceOrderRepository orders,
            ITenantContext tenant,
            TimeProvider timeProvider,
            IAuthorizationService authz,
            HttpContext http) =>
        {
            var allowed = await authz.AuthorizeAsync(
                http.User, resource: null, new ServiceOrderCreateRequirement(TenantAttachmentRequestOrderTypes.AttachmentRequestId))
                .ConfigureAwait(false);
            if (!allowed.Succeeded) return ServiceOrderRouteAccess.Refusal(allowed);

            var order = BuildAttachmentRequestOrder(body, tenant, timeProvider, http);
            await orders.AddAsync(order, http.RequestAborted).ConfigureAwait(false);
            return Results.Created($"{prefix}/attachment-requests/{order.Id}", order);
        }).RequireAuthorization(WritePermission);

        routes.MapGet($"{prefix}/attachment-requests", async (
            IServiceOrderRepository orders, ITenantContext tenant, ServiceOrderRules rules,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var mine = await orders.GetByOrderTypeAsync(TenantAttachmentRequestOrderTypes.AttachmentRequestId, ct)
                .ConfigureAwait(false);
            var readable = await ServiceOrderRouteAccess.KeepReadableAsync(
                [.. mine.Where(o => o.OrganizationId == tenant.CurrentOrganizationId)], rules, principalFactory, ct)
                .ConfigureAwait(false);
            return Results.Json(readable, opts);
        }).RequireAuthorization(ReadPermission);

        routes.MapGet($"{prefix}/attachment-requests/{{id}}", async (
            string id, IServiceOrderRepository orders, ServiceOrderRules rules,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var order = await ServiceOrderRouteAccess.FindReadableAsync(id, orders, rules, principalFactory, ct)
                .ConfigureAwait(false);
            return order is not null
                && string.Equals(order.OrderTypeId, TenantAttachmentRequestOrderTypes.AttachmentRequestId, StringComparison.Ordinal)
                    ? Results.Json(order, opts)
                    : Results.NotFound();
        }).RequireAuthorization(ReadPermission);
    }

    private static ServiceOrder BuildAttachmentRequestOrder(
        SubmitAttachmentRequestRequest body, ITenantContext tenant, TimeProvider timeProvider, HttpContext http)
    {
        var order = new ServiceOrder
        {
            Title = $"Attachment request — {body.PoleFeatureId}",
            Description = body.Description,
            OrderTypeId = TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            Status = TenantAttachmentRequestWorkflowGraph.Submitted,
            OrganizationId = tenant.CurrentOrganizationId,
            CreatedBy = http.User.Identity?.Name ?? "unknown",
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        order.Attributes["poleFeatureId"] = body.PoleFeatureId;
        return order;
    }

    private static void MapFlaggedNotices(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/flagged-notices", async (
            IClandestineAttachmentService clandestine, ITenantContext tenant, ServiceOrderRules rules,
            ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct) =>
        {
            var flagged = await clandestine.GetActiveFlaggedForOrganizationAsync(tenant.CurrentOrganizationId, ct)
                .ConfigureAwait(false);
            var readable = await ServiceOrderRouteAccess.KeepReadableAsync(flagged, rules, principalFactory, ct)
                .ConfigureAwait(false);
            return Results.Json(readable, opts);
        }).RequireAuthorization(ReadPermission);

        routes.MapPost($"{prefix}/flagged-notices/{{id}}/dispute", async (
            string id,
            SubmitDisputeRequest body,
            IServiceOrderRepository orders,
            OrderTypeRegistry orderTypes,
            IEnumerable<IOrderActionHandler> handlers,
            ServerWorkflowPrincipalFactory principalFactory,
            IAuthorizationService authz,
            TimeProvider timeProvider,
            HttpContext http) =>
        {
            var order = await orders.GetByIdAsync(id, http.RequestAborted).ConfigureAwait(false);
            if (order is null || orderTypes.Find(order.OrderTypeId) is not { Id: ClandestineAttachmentOrderTypes.ClandestineNoticeId } orderType)
                return Results.NotFound();

            var entry = new OrderActionLog(
                OrderActionType.Dispute,
                http.User.Identity?.Name ?? "unknown",
                timeProvider.GetUtcNow().UtcDateTime,
                Comment: body.EvidenceReference,
                ResultingStatus: ClandestineAttachmentWorkflowGraph.Disputed);

            var allowed = await authz.AuthorizeAsync(http.User, order, new ServiceOrderActionRequirement(entry)).ConfigureAwait(false);
            if (!allowed.Succeeded) return ServiceOrderRouteAccess.Refusal(allowed);

            var outcome = await PerformAsync(
                new OrderActionHandlerRegistry(handlers), order, orderType, entry, principalFactory, http.RequestAborted)
                .ConfigureAwait(false);
            if (outcome.Kind != OrderActionOutcomeKind.Applied) return OrderActionResults.ToResult(outcome);

            var disputed = await orders.GetByIdAsync(id, http.RequestAborted).ConfigureAwait(false);
            return disputed is null ? Results.NotFound() : Results.Json(ClandestineAttachmentService.DescribeFlag(disputed), opts);
        }).RequireAuthorization(WritePermission);
    }

    /// <summary>
    /// Performs an action the rule chain already allowed with the module handler that claims it — the same one
    /// <c>POST /service-orders/{id}/actions</c> reaches.
    /// </summary>
    private static async Task<OrderActionOutcome> PerformAsync(
        OrderActionHandlerRegistry handlers, IServiceOrder order, OrderType orderType, OrderActionLog entry,
        ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct)
    {
        var handler = handlers.Find(orderType, entry)
            ?? throw new InvalidOperationException($"No order action handler performs '{entry.Action}' on '{orderType.Id}' orders.");

        var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
        return await handler.HandleAsync(new OrderActionContext(order, orderType, entry, principal), ct).ConfigureAwait(false);
    }
}
