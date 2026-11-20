using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest.Routing;

namespace GeoAssets.Web.Services.Isp.Rest;

/// <summary>
/// <see cref="ITenantPortalRestClient"/> over <c>GeoAssets.Server</c>'s generic <c>/api/workflow</c> service-order routes
/// (XD01-365; the bespoke <c>/api/isp/tenant-portal</c> routes are gone):
/// <list type="bullet">
///   <item>submit → <c>POST service-orders</c> (the type's creation policies and attribute schema apply);</item>
///   <item>my requests → <c>GET service-orders/by-order-type/{attachment request}</c>;</item>
///   <item>flagged notices → <c>GET service-orders/by-order-type/{clandestine notice}?status=CaboARevelia</c>;</item>
///   <item>dispute → <c>POST service-orders/{id}/actions</c> with <see cref="NeutralOperatorActions.Dispute"/>, the evidence
///   reference as the comment and <see cref="ClandestineAttachmentWorkflowGraph.Disputed"/> as the resulting status.</item>
/// </list>
/// The server reads and lists only what the caller's organization gate admits. With <paramref name="routes"/> (XD01-384) each
/// request goes to the workflow API root of the order type it is about — the attachment request's, or the clandestine
/// notice's — as the kernel's order-type registry names it on the gateway (the NeutralOperator vertical's host once it runs on
/// its own); without, every request is relative to <paramref name="http"/>'s base address.
/// </summary>
internal sealed class TenantPortalRestClient(
    HttpClient http, WorkflowPrincipalFactory principals, TimeProvider timeProvider, IOrderApiRoutes? routes = null) : ITenantPortalRestClient
{
    private static readonly JsonSerializerOptions Options = GeoJsonSerializer.Options;

    public async Task<ServiceOrder> SubmitAttachmentRequestAsync(
        string poleFeatureId, string description, CancellationToken ct = default)
    {
        var principal = await principals.CreateAsync(ct).ConfigureAwait(false);
        var order = new ServiceOrder
        {
            Title = $"Attachment request — {poleFeatureId}",
            Description = description,
            OrderTypeId = TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            Status = TenantAttachmentRequestWorkflowGraph.Submitted,
            CreatedBy = principal.UserId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            OrganizationId = Guid.TryParse(principal.OrganizationId, out var organizationId) ? organizationId : Guid.Empty,
        };
        order.Attributes[TenantAttachmentRequestOrderTypes.PoleFeatureIdAttribute] = poleFeatureId;

        var uri = await RouteAsync(TenantAttachmentRequestOrderTypes.AttachmentRequestId, "service-orders", ct).ConfigureAwait(false);
        var response = await http.PostAsJsonAsync(uri, order, Options, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return order;
    }

    public Task<IReadOnlyList<ServiceOrder>> GetMyAttachmentRequestsAsync(CancellationToken ct = default) =>
        GetOrdersAsync(
            TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            $"service-orders/by-order-type/{Uri.EscapeDataString(TenantAttachmentRequestOrderTypes.AttachmentRequestId)}", ct);

    public Task<IReadOnlyList<ServiceOrder>> GetFlaggedNoticesAsync(CancellationToken ct = default) =>
        GetOrdersAsync(
            ClandestineAttachmentOrderTypes.ClandestineNoticeId,
            $"service-orders/by-order-type/{Uri.EscapeDataString(ClandestineAttachmentOrderTypes.ClandestineNoticeId)}" +
            $"?status={Uri.EscapeDataString(ClandestineAttachmentWorkflowGraph.CaboARevelia)}", ct);

    public async Task SubmitDisputeAsync(string serviceOrderId, string evidenceReference, CancellationToken ct = default)
    {
        var principal = await principals.CreateAsync(ct).ConfigureAwait(false);
        var entry = new OrderActionLog(
            NeutralOperatorActions.Dispute,
            principal.UserId,
            timeProvider.GetUtcNow().UtcDateTime,
            Comment: evidenceReference,
            ResultingStatus: ClandestineAttachmentWorkflowGraph.Disputed);

        // A disputed order is a flagged clandestine notice: it lives where that order type does.
        var uri = await RouteAsync(
            ClandestineAttachmentOrderTypes.ClandestineNoticeId, $"service-orders/{Uri.EscapeDataString(serviceOrderId)}/actions", ct)
            .ConfigureAwait(false);
        var response = await http.PostAsJsonAsync(uri, entry, Options, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<IReadOnlyList<ServiceOrder>> GetOrdersAsync(string orderTypeId, string path, CancellationToken ct)
    {
        var uri = await RouteAsync(orderTypeId, path, ct).ConfigureAwait(false);
        return await http.GetFromJsonAsync<List<ServiceOrder>>(uri, Options, ct).ConfigureAwait(false) ?? [];
    }

    private async Task<Uri> RouteAsync(string orderTypeId, string path, CancellationToken ct) =>
        routes is null
            ? new Uri(path, UriKind.Relative)
            : new Uri(await routes.ResolveAsync(orderTypeId, ct).ConfigureAwait(false), path);
}
