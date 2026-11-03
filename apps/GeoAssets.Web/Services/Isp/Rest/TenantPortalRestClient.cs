using System.Net.Http.Json;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Web.Services.Isp.Rest;

/// <summary>
/// <see cref="ITenantPortalRestClient"/> backed by <c>GeoAssets.Server</c>'s
/// <c>/api/isp/tenant-portal/*</c> endpoints (XD01-269).
/// </summary>
internal sealed class TenantPortalRestClient(HttpClient http) : ITenantPortalRestClient
{
    private sealed record SubmitAttachmentRequestRequest(string PoleFeatureId, string Description);

    private sealed record SubmitDisputeRequest(string EvidenceReference);

    public async Task<ServiceOrder> SubmitAttachmentRequestAsync(
        string poleFeatureId, string description, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            "attachment-requests", new SubmitAttachmentRequestRequest(poleFeatureId, description), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ServiceOrder>(ct)
            ?? throw new InvalidOperationException("POST attachment-requests returned an empty response.");
    }

    public async Task<IReadOnlyList<ServiceOrder>> GetMyAttachmentRequestsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ServiceOrder>>("attachment-requests", ct).ConfigureAwait(false)
        ?? [];

    public async Task<IReadOnlyList<ServiceOrder>> GetFlaggedNoticesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ServiceOrder>>("flagged-notices", ct).ConfigureAwait(false)
        ?? [];

    public async Task<ClandestineFlagResult> SubmitDisputeAsync(
        string serviceOrderId, string evidenceReference, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            $"flagged-notices/{serviceOrderId}/dispute", new SubmitDisputeRequest(evidenceReference), ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ClandestineFlagResult>(ct)
            ?? throw new InvalidOperationException("POST flagged-notices/dispute returned an empty response.");
    }
}
