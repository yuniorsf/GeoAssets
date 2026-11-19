using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The tenant ISP attachment-request order type (XD01-269) — one order per request a tenant
/// submits to attach to a pole, built on <see cref="TenantAttachmentRequestWorkflowGraph"/>.
/// Modeled as a <c>ServiceOrder</c>, like every other order type in this epic, rather than a
/// bespoke aggregate: it gets optimistic concurrency, an audit trail, and real tenant isolation
/// via <c>IServiceOrder.OrganizationId</c> (<see cref="GeoAssets.Core.Interfaces.IOrgOwnedResource"/>)
/// "for free," exactly the reasoning the epic's own architecture notes give for Levantamento/
/// Fiscalização/Ofício.
/// Only an <see cref="IspRoles.Tenant"/> may submit one (XD01-364, <see cref="OrderType.CreationPolicies"/>); a recipient
/// of its dispatch only reads and comments on it.
/// </summary>
public static class TenantAttachmentRequestOrderTypes
{
    public const string AttachmentRequestId = "isp-tenant-attachment-request";

    public static OrderType CreateAttachmentRequest() => new()
    {
        Id = AttachmentRequestId,
        DisplayName = "Tenant Attachment Request",
        Description = "A retail ISP tenant's request to attach to a neutral-operator pole.",
        InitialStateKey = TenantAttachmentRequestWorkflowGraph.Submitted,
        States = TenantAttachmentRequestWorkflowGraph.States(),
        Transitions = TenantAttachmentRequestWorkflowGraph.Transitions(),
        CreationPolicies = [new(PolicyKind.Role, IspRoles.Tenant)],
        RecipientAccess = OrderRecipientAccess.Inform,
    };

    public static void RegisterAll(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(CreateAttachmentRequest());
    }
}
