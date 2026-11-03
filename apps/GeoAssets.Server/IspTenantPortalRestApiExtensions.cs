using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Maps the tenant ISP self-service portal's REST surface (XD01-269) at
/// <c>/api/isp/tenant-portal</c> — attachment-request submission/listing and clandestine-flag
/// disputing, both scoped by the caller's own <c>OrganizationId</c>
/// (<see cref="ITenantContext"/>/<see cref="IOrgOwnedResource"/>), the same real multi-tenant
/// isolation boundary <see cref="OrgResourceAuthorizationHandler"/> enforces elsewhere in this
/// codebase (XD01-21) — not a bespoke isolation mechanism invented for this ticket.
///
/// "Poles flagged for regularization affecting them" and "dispute submissions" both operate on
/// the existing clandestine-notice <c>ServiceOrder</c>s from XD01-263 — <see cref="ClandestineAttachmentOrderTypes"/>
/// orders were never actually wired into any host's <c>OrderTypeRegistry</c> before this ticket
/// (see <c>Program.cs</c>'s registration comment); this is the first REST surface over them.
/// "Attachment requests" are modeled as their own new <c>ServiceOrder</c> type
/// (<see cref="TenantAttachmentRequestOrderTypes"/>) rather than a bespoke aggregate, for the
/// same reason every other order type in this epic is: optimistic concurrency, an audit trail,
/// and real org-scoped isolation "for free."
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
            HttpContext http) =>
        {
            var order = BuildAttachmentRequestOrder(body, tenant, timeProvider, http);
            await orders.AddAsync(order).ConfigureAwait(false);
            return Results.Created($"{prefix}/attachment-requests/{order.Id}", order);
        }).RequireAuthorization(WritePermission);

        routes.MapGet($"{prefix}/attachment-requests", async (
            IServiceOrderRepository orders, ITenantContext tenant) =>
        {
            var mine = await orders.GetByOrderTypeAsync(TenantAttachmentRequestOrderTypes.AttachmentRequestId)
                .ConfigureAwait(false);
            return Results.Json(mine.Where(o => o.OrganizationId == tenant.CurrentOrganizationId).ToList(), opts);
        }).RequireAuthorization(ReadPermission);

        routes.MapGet($"{prefix}/attachment-requests/{{id}}", async (
            string id, IServiceOrderRepository orders, IAuthorizationService authz, HttpContext http) =>
        {
            var order = await orders.GetByIdAsync(id).ConfigureAwait(false);
            if (order is null) return Results.NotFound();

            var result = await authz.AuthorizeAsync(http.User, order, new OrgResourceRequirement(ReadPermission))
                .ConfigureAwait(false);
            if (!result.Succeeded)
                return Results.Json(new { reason = "Not authorized to read this attachment request." },
                    statusCode: StatusCodes.Status403Forbidden);

            return Results.Json(order, opts);
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
            IClandestineAttachmentService clandestine, ITenantContext tenant) =>
        {
            var flagged = await clandestine.GetActiveFlaggedForOrganizationAsync(tenant.CurrentOrganizationId)
                .ConfigureAwait(false);
            return Results.Json(flagged, opts);
        }).RequireAuthorization(ReadPermission);

        routes.MapPost($"{prefix}/flagged-notices/{{id}}/dispute", async (
            string id,
            SubmitDisputeRequest body,
            IServiceOrderRepository orders,
            IClandestineAttachmentService clandestine,
            IAuthorizationService authz,
            HttpContext http) => await HandleDisputeAsync(id, body, orders, clandestine, authz, http, opts)
                .ConfigureAwait(false))
            .RequireAuthorization(WritePermission);
    }

    private static async Task<IResult> HandleDisputeAsync(
        string id,
        SubmitDisputeRequest body,
        IServiceOrderRepository orders,
        IClandestineAttachmentService clandestine,
        IAuthorizationService authz,
        HttpContext http,
        JsonSerializerOptions opts)
    {
        var order = await orders.GetByIdAsync(id).ConfigureAwait(false);
        if (order is null) return Results.NotFound();

        var result = await authz.AuthorizeAsync(http.User, order, new OrgResourceRequirement(WritePermission))
            .ConfigureAwait(false);
        if (!result.Succeeded)
            return Results.Json(new { reason = "Not authorized to dispute this notice." },
                statusCode: StatusCodes.Status403Forbidden);

        try
        {
            var disputeResult = await clandestine.DisputeAsync(
                id, body.EvidenceReference, http.User.Identity?.Name).ConfigureAwait(false);
            return Results.Json(disputeResult, opts);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { reason = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
