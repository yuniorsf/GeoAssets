using GeoAssets.Workflow.Orders;

namespace GeoAssets.NeutralOperator.Application;

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

    /// <summary>The attribute naming the pole a request is for — required on every request (XD01-365).</summary>
    public const string PoleFeatureIdAttribute = "poleFeatureId";

    /// <summary>
    /// Requires a non-empty <see cref="PoleFeatureIdAttribute"/>. No <c>type</c>: the validator reads an attribute value that
    /// parses as JSON as that JSON, so a numeric-looking pole id must still pass.
    /// </summary>
    public const string AttributesSchema = $$"""
        {
          "type": "object",
          "properties": {
            "{{PoleFeatureIdAttribute}}": { "minLength": 1 }
          },
          "required": ["{{PoleFeatureIdAttribute}}"]
        }
        """;

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
        AttributesSchemaJson = AttributesSchema,
    };

    public static void RegisterAll(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(CreateAttachmentRequest());
    }
}
