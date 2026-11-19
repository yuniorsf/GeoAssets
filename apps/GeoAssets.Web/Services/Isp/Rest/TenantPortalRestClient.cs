using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;

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
/// The server reads and lists only what the caller's organization gate admits.
/// </summary>
internal sealed class TenantPortalRestClient(
    HttpClient http, WorkflowPrincipalFactory principals, TimeProvider timeProvider) : ITenantPortalRestClient
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

        var response = await http.PostAsJsonAsync("service-orders", order, Options, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return order;
    }

    public Task<IReadOnlyList<ServiceOrder>> GetMyAttachmentRequestsAsync(CancellationToken ct = default) =>
        GetOrdersAsync(
            $"service-orders/by-order-type/{Uri.EscapeDataString(TenantAttachmentRequestOrderTypes.AttachmentRequestId)}", ct);

    public Task<IReadOnlyList<ServiceOrder>> GetFlaggedNoticesAsync(CancellationToken ct = default) =>
        GetOrdersAsync(
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

        var response = await http.PostAsJsonAsync(
            $"service-orders/{Uri.EscapeDataString(serviceOrderId)}/actions", entry, Options, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<IReadOnlyList<ServiceOrder>> GetOrdersAsync(string path, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<ServiceOrder>>(path, Options, ct).ConfigureAwait(false) ?? [];
}
