using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// One child order of a fan-out: the order addressed to one occupant organization, and the
/// targets it holds — that occupant's poles among the parent's, and no other.
/// </summary>
public sealed record SharedUsePlannedChild
{
    /// <summary>The organization the child order is dispatched to.</summary>
    public required Guid OccupantOrganizationId { get; init; }

    /// <summary><see cref="SharedUseDispatchPlanner.ChildOrderId"/> of the parent and that organization.</summary>
    public required string ChildOrderId { get; init; }

    /// <summary>
    /// True when the parent already has this child: a repeated dispatch neither creates it again
    /// nor notifies its organization again, and leaves it as it is.
    /// </summary>
    public required bool AlreadyDispatched { get; init; }

    /// <summary>
    /// The child order as it is to be stored: owned by the parent's organization, carrying its
    /// one dispatch to <see cref="OccupantOrganizationId"/>.
    /// </summary>
    public required ServiceOrder Order { get; init; }

    /// <summary>The child's targets, in pole feature id order.</summary>
    public required IReadOnlyList<ServiceOrderTarget> Targets { get; init; }
}
