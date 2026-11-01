using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// The 12-state field-crew dispatch workflow graph (XD01-220):
/// Draft→Pending→Scheduled→Dispatched→EnRoute→OnSite→InProgress⇄Suspended→Completed→Inspected|ConflictReview,
/// plus Cancelled. Built on the existing per-<see cref="OrderType"/> custom-graph mechanism
/// (<see cref="OrderType.States"/>/<see cref="OrderType.Transitions"/>, consulted by
/// <see cref="ServiceOrderTransitions.IsValid(OrderType?, string, string)"/> and
/// <see cref="ValidatingServiceOrderRepository"/>) rather than a new mechanism of its own.
///
/// Cancellation is deliberately only legal before the crew commits to on-site work (Draft through
/// EnRoute) — once <see cref="FieldWorkStatus.OnSite"/>, real work/safety procedures may already
/// be underway, so the graph offers <see cref="FieldWorkStatus.Suspended"/> instead of a cancel
/// path out of OnSite/InProgress/Completed. This is a judgment call about field-ops practice, not
/// a requirement stated by the ticket's literal state sequence — revisit with whoever owns the
/// real dispatch workflow if it doesn't match their process.
///
/// <see cref="FieldWorkStatus.EnRoute"/>→<see cref="FieldWorkStatus.OnSite"/> and
/// <see cref="FieldWorkStatus.OnSite"/>→<see cref="FieldWorkStatus.InProgress"/> are structurally
/// legal edges in this graph but are additionally gated by <see cref="IFieldWorkTransitionGuard"/>
/// — see <see cref="FieldWorkOrderTransitioner"/>, the entry point that enforces both the graph
/// and the guard together.
/// </summary>
public static class FieldWorkWorkflowGraph
{
    public static readonly IReadOnlyList<WorkflowState> States =
    [
        new(FieldWorkStatus.Draft, "Draft"),
        new(FieldWorkStatus.Pending, "Pending"),
        new(FieldWorkStatus.Scheduled, "Scheduled"),
        new(FieldWorkStatus.Dispatched, "Dispatched"),
        new(FieldWorkStatus.EnRoute, "En Route"),
        new(FieldWorkStatus.OnSite, "On Site"),
        new(FieldWorkStatus.InProgress, "In Progress"),
        new(FieldWorkStatus.Suspended, "Suspended"),
        new(FieldWorkStatus.Completed, "Completed"),
        new(FieldWorkStatus.Inspected, "Inspected", IsSuccess: true),
        new(FieldWorkStatus.ConflictReview, "Conflict Review"),
        new(FieldWorkStatus.Cancelled, "Cancelled"),
    ];

    public static readonly IReadOnlyList<WorkflowTransition> Transitions =
    [
        new(FieldWorkStatus.Draft, FieldWorkStatus.Pending),
        new(FieldWorkStatus.Draft, FieldWorkStatus.Cancelled, OrderActionType.Cancel),

        new(FieldWorkStatus.Pending, FieldWorkStatus.Scheduled),
        new(FieldWorkStatus.Pending, FieldWorkStatus.Cancelled, OrderActionType.Cancel),

        new(FieldWorkStatus.Scheduled, FieldWorkStatus.Dispatched, OrderActionType.Dispatch),
        new(FieldWorkStatus.Scheduled, FieldWorkStatus.Cancelled, OrderActionType.Cancel),

        new(FieldWorkStatus.Dispatched, FieldWorkStatus.EnRoute, OrderActionType.Accept),
        new(FieldWorkStatus.Dispatched, FieldWorkStatus.Cancelled, OrderActionType.Cancel),

        new(FieldWorkStatus.EnRoute, FieldWorkStatus.OnSite),
        new(FieldWorkStatus.EnRoute, FieldWorkStatus.Cancelled, OrderActionType.Cancel),

        new(FieldWorkStatus.OnSite, FieldWorkStatus.InProgress, OrderActionType.Execute),

        new(FieldWorkStatus.InProgress, FieldWorkStatus.Suspended),
        new(FieldWorkStatus.Suspended, FieldWorkStatus.InProgress),
        new(FieldWorkStatus.InProgress, FieldWorkStatus.Completed, OrderActionType.Complete),

        new(FieldWorkStatus.Completed, FieldWorkStatus.Inspected, OrderActionType.Approve),
        new(FieldWorkStatus.Completed, FieldWorkStatus.ConflictReview, OrderActionType.Reject),
    ];

    public const string InitialStateKey = FieldWorkStatus.Draft;

    /// <summary>
    /// Builds an <see cref="OrderType"/> wired with this graph, ready to register in an
    /// <see cref="OrderTypeRegistry"/>. <paramref name="creationPolicies"/>/
    /// <paramref name="actionPermissions"/>/<paramref name="attributesSchemaJson"/> are passed
    /// through unchanged (empty/null = unrestricted, same convention as every other
    /// <see cref="OrderType"/> field) — this factory only owns the workflow graph, not a host's
    /// authorization or attribute-schema policy.
    /// </summary>
    public static OrderType BuildOrderType(
        string id,
        string displayName,
        string description = "",
        List<OrderCreationPolicy>? creationPolicies = null,
        List<OrderActionPermission>? actionPermissions = null,
        string? attributesSchemaJson = null) => new()
        {
            Id = id,
            DisplayName = displayName,
            Description = description,
            CreationPolicies = creationPolicies ?? [],
            ActionPermissions = actionPermissions ?? [],
            AttributesSchemaJson = attributesSchemaJson,
            States = [.. States],
            Transitions = [.. Transitions],
            InitialStateKey = InitialStateKey,
        };
}
