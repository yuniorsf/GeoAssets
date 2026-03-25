using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence.Entities;

namespace GeoAssets.Workflow.Persistence;

internal static class OrderTypeMapper
{
    // ── Domain → EF ───────────────────────────────────────────────────────────

    public static OrderTypeRecord ToRecord(OrderType t) => new()
    {
        Id          = t.Id,
        DisplayName = t.DisplayName,
        Description = t.Description,
        CreationPolicies = t.CreationPolicies
            .Select(p => new OrderCreationPolicyRecord
            {
                OrderTypeId = t.Id,
                Kind        = (int)p.Kind,
                Value       = p.Value,
            }).ToList(),
        ActionPermissions = t.ActionPermissions
            .Select(p => new OrderActionPermissionRecord
            {
                OrderTypeId = t.Id,
                Action      = (int)p.Action,
                Kind        = (int)p.Kind,
                Value       = p.Value,
            }).ToList(),
    };

    // ── EF → Domain ───────────────────────────────────────────────────────────

    public static OrderType ToDomain(OrderTypeRecord r) => new()
    {
        Id          = r.Id,
        DisplayName = r.DisplayName,
        Description = r.Description,
        CreationPolicies = r.CreationPolicies
            .Select(p => new OrderCreationPolicy((PolicyKind)p.Kind, p.Value))
            .ToList(),
        ActionPermissions = r.ActionPermissions
            .Select(p => new OrderActionPermission((OrderActionType)p.Action, (PolicyKind)p.Kind, p.Value))
            .ToList(),
    };
}
