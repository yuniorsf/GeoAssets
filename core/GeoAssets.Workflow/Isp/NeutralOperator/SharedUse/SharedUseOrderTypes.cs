using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// The two order types of shared pole use — "uso mútuo" (XD01-316).
///
/// <see cref="PoleWorkId"/> is the pole owner's order: its targets are poles, each with the
/// activities to carry out, and dispatching it fans out into one <see cref="OccupantNoticeId"/>
/// order per occupant organization that has a contracted attachment on those poles. The notice
/// is owned by the pole owner and dispatched to the occupant, who reads it and its poles.
///
/// Both target every asset type carrying the <see cref="OccupancyAssetTypeRoles.OccupiableSupport"/>
/// role (XD01-368): which types are poles is the asset-type catalog's to say, by tagging them.
/// </summary>
public static class SharedUseOrderTypes
{
    public const string PoleWorkId = "shared-use-pole-work";

    public const string OccupantNoticeId = "shared-use-occupant-notice";

    /// <summary>Attribute of a pole whose value is shown as the target's alias: the pole's field number.</summary>
    public const string PoleAliasAttributeKey = "barramento";

    /// <summary>
    /// The success state of a pole-work order (XD01-318): reached only by approving a completed
    /// order, which commits the occupancy changes staged on its poles. A completed pole-work
    /// order is not finished yet — it waits for that approval.
    /// </summary>
    public const string ApprovedStateKey = "Approved";

    /// <summary>How many poles one selection may add to a pole-work order.</summary>
    public const int PoleSelectionLimit = 5000;

    /// <summary>The work an occupant can be asked to do on a pole. Shared by both types, so a notice's targets keep the activities of the order they came from.</summary>
    public static IReadOnlyList<OrderTargetActivity> Activities { get; } =
    [
        new("relocate-attachment", "Relocate attachment"),
        new("adjust-attachment", "Adjust attachment height or tension"),
        new("remove-attachment", "Remove attachment"),
        new("identify-attachment", "Identify and tag attachment"),
    ];

    public static OrderType CreatePoleWork() => new()
    {
        Id = PoleWorkId,
        DisplayName = "Shared-use pole work",
        Description = "Work on poles shared with other operators, dispatched to each occupant with an active contract.",
        CreationPolicies =
        [
            new(PolicyKind.Role, "Supervisor"),
            new(PolicyKind.Role, "Administrator"),
        ],
        InitialStateKey = ServiceOrderStatus.Draft,
        // XD01-333: a pole-work order is never dispatched to anyone — its fan-out creates notices,
        // and the order itself gets no dispatch — so no recipient ever reaches it.
        RecipientAccess = OrderRecipientAccess.Inform,
        States = PoleWorkStates(),
        Transitions = PoleWorkTransitions(),
        // Approving it commits the occupancy staged on its poles (XD01-361).
        Capabilities = [OccupancyCapabilities.Commit],
        TargetPolicy = new OrderTargetPolicy
        {
            TargetAssetTypeRoles = [OccupancyAssetTypeRoles.OccupiableSupport],
            AliasAttributeKey = PoleAliasAttributeKey,
            SelectionLimit = PoleSelectionLimit,
            Activities = Activities,
            Exclusivity = OrderTargetExclusivity.WarnOnOverlap,
            FanOut = new OrderTargetFanOutPolicy(OccupantNoticeId),
        },
    };

    public static OrderType CreateOccupantNotice() => new()
    {
        Id = OccupantNoticeId,
        DisplayName = "Shared-use occupant notice",
        Description = "The work one occupant is asked to do on the poles of a shared-use order.",
        CreationPolicies =
        [
            new(PolicyKind.Role, "Supervisor"),
            new(PolicyKind.Role, "Administrator"),
        ],
        // A notice exists only once dispatched to its occupant, so it starts where a dispatch leaves an order.
        InitialStateKey = ServiceOrderStatus.Pending,
        // XD01-333: the occupant reads the notice, its poles and the work asked on each, and
        // records nothing in it — the occupancy that work changes is staged and approved by the
        // pole owner (XD01-315/318).
        RecipientAccess = OrderRecipientAccess.Inform,
        // XD01-336: the occupant accepts the notice and comments on it, and cancels nothing.
        RecipientActions = [OrderAction.Accept, OrderAction.Annotate],
        States = States(),
        Transitions = NoticeTransitions(),
        // Approving it commits the occupancy staged on its poles (XD01-361).
        Capabilities = [OccupancyCapabilities.Commit],
        TargetPolicy = new OrderTargetPolicy
        {
            TargetAssetTypeRoles = [OccupancyAssetTypeRoles.OccupiableSupport],
            AliasAttributeKey = PoleAliasAttributeKey,
            Activities = Activities,
        },
    };

    /// <summary>Registers both types. The notice goes in first only for readability: the registry resolves a fan-out's child when the fan-out runs.</summary>
    public static void RegisterAll(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(CreateOccupantNotice());
        registry.Register(CreatePoleWork());
    }

    private static List<WorkflowState> States() =>
    [
        new(ServiceOrderStatus.Draft, "Draft"),
        new(ServiceOrderStatus.Pending, "Dispatched"),
        new(ServiceOrderStatus.InProgress, "In progress"),
        new(ServiceOrderStatus.OnHold, "On hold"),
        new(ServiceOrderStatus.Completed, "Completed", IsSuccess: true),
        new(ServiceOrderStatus.Cancelled, "Cancelled"),
    ];

    /// <summary>
    /// The notice's states, except that completing pole work is not success: success is
    /// <see cref="ApprovedStateKey"/>, so closing the order is the approval that commits its staged
    /// occupancy (XD01-318).
    /// </summary>
    private static List<WorkflowState> PoleWorkStates() =>
    [
        new(ServiceOrderStatus.Draft, "Draft"),
        new(ServiceOrderStatus.Pending, "Dispatched"),
        new(ServiceOrderStatus.InProgress, "In progress"),
        new(ServiceOrderStatus.OnHold, "On hold"),
        new(ServiceOrderStatus.Completed, "Awaiting approval"),
        new(ApprovedStateKey, "Approved", IsSuccess: true),
        new(ServiceOrderStatus.Cancelled, "Cancelled"),
    ];

    /// <summary>
    /// The work's graph plus the occupant's acceptance (XD01-336): accepting a dispatched notice
    /// puts it in progress, as starting it does.
    /// </summary>
    private static List<WorkflowTransition> NoticeTransitions() =>
    [
        new(ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, OrderAction.Accept),
        .. WorkTransitions(),
    ];

    private static List<WorkflowTransition> WorkTransitions() =>
    [
        new(ServiceOrderStatus.Pending, ServiceOrderStatus.InProgress, OrderAction.Execute),
        new(ServiceOrderStatus.Pending, ServiceOrderStatus.Cancelled, OrderAction.Cancel),
        new(ServiceOrderStatus.InProgress, ServiceOrderStatus.OnHold),
        new(ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed, OrderAction.Complete),
        new(ServiceOrderStatus.InProgress, ServiceOrderStatus.Cancelled, OrderAction.Cancel),
        new(ServiceOrderStatus.OnHold, ServiceOrderStatus.InProgress),
        new(ServiceOrderStatus.OnHold, ServiceOrderStatus.Cancelled, OrderAction.Cancel),
    ];

    /// <summary>
    /// The work's graph plus the two dispatch edges — a draft becomes dispatched, and a
    /// dispatched order can be dispatched again, which is how occupants of poles added later get
    /// their notice — and the approval of completed work (XD01-318): approving commits the staged
    /// occupancy, rejecting sends the work back to the crew, and a completed order can still be
    /// cancelled.
    /// </summary>
    private static List<WorkflowTransition> PoleWorkTransitions() =>
    [
        new(ServiceOrderStatus.Draft, ServiceOrderStatus.Pending, OrderAction.Dispatch),
        new(ServiceOrderStatus.Draft, ServiceOrderStatus.Cancelled, OrderAction.Cancel),
        new(ServiceOrderStatus.Pending, ServiceOrderStatus.Pending, OrderAction.Dispatch),
        .. WorkTransitions(),
        new(ServiceOrderStatus.Completed, ApprovedStateKey, OrderAction.Approve),
        new(ServiceOrderStatus.Completed, ServiceOrderStatus.InProgress, OrderAction.Reject),
        new(ServiceOrderStatus.Completed, ServiceOrderStatus.Cancelled, OrderAction.Cancel),
    ];
}
