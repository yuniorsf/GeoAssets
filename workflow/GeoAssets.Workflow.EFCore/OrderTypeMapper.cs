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
        AttributesSchemaJson = t.AttributesSchemaJson,
        TargetPolicyJson = OrderTargetPolicyMapper.ToJson(t.TargetPolicy),
        RecipientAccess = (int)t.RecipientAccess,
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

    /// <summary>
    /// The <c>OrderTypeRecipientActions</c> rows of <paramref name="t"/> (XD01-336): per action of
    /// <see cref="OrderType.RecipientActions"/>, one row granting it with no move, then one per
    /// transition of the type tagged with it (<see cref="ServiceOrderTransitions.TransitionsFor"/>)
    /// — what the row-level security of "ServiceOrders" checks a recipient's status change against.
    /// </summary>
    public static List<OrderTypeRecipientActionRecord> ToRecipientActionRecords(OrderType t) =>
    [
        .. t.RecipientActions.Distinct().SelectMany(action => (IEnumerable<OrderTypeRecipientActionRecord>)
        [
            new() { OrderTypeId = t.Id, Action = (int)action },
            .. ServiceOrderTransitions.TransitionsFor(t, action).Select(x => new OrderTypeRecipientActionRecord
            {
                OrderTypeId  = t.Id,
                Action       = (int)action,
                FromStateKey = x.FromStateKey,
                ToStateKey   = x.ToStateKey,
            }),
        ]),
    ];

    // ── EF → Domain ───────────────────────────────────────────────────────────

    /// <summary>A type with no <c>OrderTypeRecipientActions</c> rows: its recipients take no action of their own.</summary>
    public static OrderType ToDomain(OrderTypeRecord r) => ToDomain(r, []);

    public static OrderType ToDomain(OrderTypeRecord r, IEnumerable<OrderTypeRecipientActionRecord> recipientActions) => new()
    {
        Id              = r.Id,
        DisplayName     = r.DisplayName,
        Description     = r.Description,
        InitialStateKey = r.InitialStateKey,
        AttributesSchemaJson = r.AttributesSchemaJson,
        TargetPolicy = OrderTargetPolicyMapper.FromJson(r.TargetPolicyJson),
        RecipientAccess = RecipientAccessFrom(r.RecipientAccess),
        RecipientActions = RecipientActionsFrom(recipientActions),
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

    /// <summary>
    /// The stored number as an <see cref="OrderRecipientAccess"/>; a number outside the
    /// enumeration (written by hand) reads as <see cref="OrderRecipientAccess.Inform"/>, never as
    /// more access — as the row-level security functions read it.
    /// </summary>
    public static OrderRecipientAccess RecipientAccessFrom(int stored) =>
        Enum.IsDefined((OrderRecipientAccess)stored) ? (OrderRecipientAccess)stored : OrderRecipientAccess.Inform;

    /// <summary>
    /// The distinct actions of <paramref name="stored"/>, in the order they were written; a number
    /// outside <see cref="OrderActionType"/> (written by hand) is dropped — never more than stored.
    /// </summary>
    public static List<OrderActionType> RecipientActionsFrom(IEnumerable<OrderTypeRecipientActionRecord> stored) =>
    [
        .. stored
            .OrderBy(a => a.Id)
            .Select(a => (OrderActionType)a.Action)
            .Where(action => Enum.IsDefined(action))
            .Distinct(),
    ];
}
