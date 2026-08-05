using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence.Entities;

namespace GeoAssets.Workflow.Persistence;

internal static class OrderTypeMapper
{
    // ── Domain → EF ───────────────────────────────────────────────────────────

    public static OrderTypeRecord ToRecord(OrderType t) => new()
    {
        Id              = t.Id,
        DisplayName     = t.DisplayName,
        Description     = t.Description,
        InitialStateKey = t.InitialStateKey,
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
                OrderTypeId  = t.Id,
                Action       = (int)p.Action,
                Kind         = (int)p.Kind,
                Value        = p.Value,
                FromStateKey = p.FromStateKey,
            }).ToList(),
        States = t.States
            .Select(s => new OrderTypeStateRecord
            {
                OrderTypeId = t.Id,
                Key         = s.Key,
                DisplayName = s.DisplayName,
                IsSuccess   = s.IsSuccess,
            }).ToList(),
        Transitions = t.Transitions
            .Select(x => new OrderTypeTransitionRecord
            {
                OrderTypeId   = t.Id,
                FromStateKey  = x.FromStateKey,
                ToStateKey    = x.ToStateKey,
                TriggerAction = x.TriggerAction.HasValue ? (int)x.TriggerAction.Value : null,
            }).ToList(),
    };

    // ── EF → Domain ───────────────────────────────────────────────────────────

    public static OrderType ToDomain(OrderTypeRecord r) => new()
    {
        Id              = r.Id,
        DisplayName     = r.DisplayName,
        Description     = r.Description,
        InitialStateKey = r.InitialStateKey,
        CreationPolicies = r.CreationPolicies
            .Select(p => new OrderCreationPolicy((PolicyKind)p.Kind, p.Value))
            .ToList(),
        ActionPermissions = r.ActionPermissions
            .Select(p => new OrderActionPermission((OrderActionType)p.Action, (PolicyKind)p.Kind, p.Value, p.FromStateKey))
            .ToList(),
        States = r.States
            .Select(s => new WorkflowState(s.Key, s.DisplayName, s.IsSuccess))
            .ToList(),
        Transitions = r.Transitions
            .Select(x => new WorkflowTransition(
                x.FromStateKey,
                x.ToStateKey,
                x.TriggerAction.HasValue ? (OrderActionType)x.TriggerAction.Value : null))
            .ToList(),
    };
}
