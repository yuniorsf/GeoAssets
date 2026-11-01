using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// The FTTH drop-install work-order state machine (XD01-273): CRM sale → CTO port test → drop
/// launch → tag affixed → PTO power → ONT provisioned → activated, a strict linear chain with no
/// branching or cancellation edge (unlike <see cref="FieldWorkWorkflowGraph"/>'s 12-state graph —
/// the ticket's own state list names no such edge, and none is added speculatively). Built on
/// the same existing per-<see cref="OrderType"/> custom-graph mechanism
/// (<see cref="OrderType.States"/>/<see cref="OrderType.Transitions"/>) <see cref="FieldWorkWorkflowGraph"/>
/// uses, consulted by <see cref="ServiceOrderTransitions.IsValid(OrderType?, string, string)"/>.
///
/// <see cref="FtthDropInstallStatus.TagAffixed"/>→<see cref="FtthDropInstallStatus.PtoPower"/> is
/// structurally legal here but additionally gated by <see cref="FtthDropInstallTransitioner"/> —
/// that is the entry point enforcing both the graph and the guard together.
/// </summary>
public static class FtthDropInstallWorkflowGraph
{
    public static readonly IReadOnlyList<WorkflowState> States =
    [
        new(FtthDropInstallStatus.CrmSale, "CRM Sale"),
        new(FtthDropInstallStatus.CtoPortTest, "CTO Port Test"),
        new(FtthDropInstallStatus.DropLaunch, "Drop Launch"),
        new(FtthDropInstallStatus.TagAffixed, "Tag Affixed"),
        new(FtthDropInstallStatus.PtoPower, "PTO Power"),
        new(FtthDropInstallStatus.OntProvisioned, "ONT Provisioned"),
        new(FtthDropInstallStatus.Activated, "Activated", IsSuccess: true),
    ];

    public static readonly IReadOnlyList<WorkflowTransition> Transitions =
    [
        new(FtthDropInstallStatus.CrmSale, FtthDropInstallStatus.CtoPortTest),
        new(FtthDropInstallStatus.CtoPortTest, FtthDropInstallStatus.DropLaunch),
        new(FtthDropInstallStatus.DropLaunch, FtthDropInstallStatus.TagAffixed),
        new(FtthDropInstallStatus.TagAffixed, FtthDropInstallStatus.PtoPower),
        new(FtthDropInstallStatus.PtoPower, FtthDropInstallStatus.OntProvisioned),
        new(FtthDropInstallStatus.OntProvisioned, FtthDropInstallStatus.Activated, OrderActionType.Complete),
    ];

    public const string InitialStateKey = FtthDropInstallStatus.CrmSale;

    /// <summary>
    /// Builds an <see cref="OrderType"/> wired with this graph, ready to register in an
    /// <see cref="OrderTypeRegistry"/>. <paramref name="creationPolicies"/>/
    /// <paramref name="actionPermissions"/>/<paramref name="attributesSchemaJson"/> are passed
    /// through unchanged (empty/null = unrestricted) — this factory only owns the workflow
    /// graph, not a host's authorization or attribute-schema policy.
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
