using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// A tenant ISP's gateway to <c>GeoAssets.Server</c> (XD01-269) — submitting/listing attachment requests, viewing poles
/// flagged for regularization affecting the caller's own organization, and disputing a flag. Since XD01-365 the portal
/// has no routes of its own: every operation is a generic <c>/api/workflow</c> service-order operation, decided by the
/// same rule chain as any other order.
/// </summary>
public interface ITenantPortalRestClient
{
    /// <summary>Creates a <see cref="TenantAttachmentRequestOrderTypes.AttachmentRequestId"/> order for the pole.</summary>
    Task<ServiceOrder> SubmitAttachmentRequestAsync(string poleFeatureId, string description, CancellationToken ct = default);

    /// <summary>The attachment requests the caller may read.</summary>
    Task<IReadOnlyList<ServiceOrder>> GetMyAttachmentRequestsAsync(CancellationToken ct = default);

    /// <summary>The clandestine notices in <see cref="ClandestineAttachmentWorkflowGraph.CaboARevelia"/> the caller may read.</summary>
    Task<IReadOnlyList<ServiceOrder>> GetFlaggedNoticesAsync(CancellationToken ct = default);

    /// <summary>Disputes a flagged notice: the <see cref="OrderActionType.Dispute"/> action, with the evidence reference as its comment.</summary>
    Task SubmitDisputeAsync(string serviceOrderId, string evidenceReference, CancellationToken ct = default);
}
