namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>One occupant organization a shared-use order is, or would be, dispatched to.</summary>
/// <param name="OccupantOrganizationId">The organization.</param>
/// <param name="ChildOrderId">The id of its child order, existing or to be created.</param>
/// <param name="PoleCount">How many of the order's poles it has work on.</param>
/// <param name="AlreadyDispatched">True when the child order already exists; a dispatch leaves it as it is.</param>
public sealed record SharedUseDispatchRecipient(
    Guid OccupantOrganizationId,
    string ChildOrderId,
    int PoleCount,
    bool AlreadyDispatched);
