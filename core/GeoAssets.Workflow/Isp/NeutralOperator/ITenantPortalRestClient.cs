using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// A tenant ISP's gateway to <c>GeoAssets.Server</c>'s <c>/api/isp/tenant-portal/*</c>
/// endpoints (XD01-269) — submitting/listing attachment requests, viewing poles flagged for
/// regularization affecting the caller's own organization, and disputing a flag.
/// </summary>
public interface ITenantPortalRestClient
{
    Task<ServiceOrder> SubmitAttachmentRequestAsync(string poleFeatureId, string description, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceOrder>> GetMyAttachmentRequestsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ServiceOrder>> GetFlaggedNoticesAsync(CancellationToken ct = default);

    Task<ClandestineFlagResult> SubmitDisputeAsync(string serviceOrderId, string evidenceReference, CancellationToken ct = default);
}
