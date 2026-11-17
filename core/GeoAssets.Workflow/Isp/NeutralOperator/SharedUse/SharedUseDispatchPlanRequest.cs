using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>Everything <see cref="SharedUseDispatchPlanner.Plan"/> decides from. All of it is data already read.</summary>
public sealed record SharedUseDispatchPlanRequest
{
    /// <summary>The order to fan out.</summary>
    public required IServiceOrder Parent { get; init; }

    /// <summary>The parent's order type; its target policy must declare a fan-out.</summary>
    public required OrderType ParentType { get; init; }

    /// <summary>The order type of the children, as the parent type's fan-out policy names it.</summary>
    public required OrderType ChildType { get; init; }

    /// <summary>Every target of the parent.</summary>
    public required IReadOnlyCollection<ServiceOrderTarget> Targets { get; init; }

    /// <summary>
    /// The conflict level of each target's feature as evaluated now, by feature id. A feature that
    /// is missing has no conflict. The level stored on a target is a snapshot and is not consulted.
    /// </summary>
    public required IReadOnlyDictionary<string, OrderTargetConflictLevel> ConflictLevels { get; init; }

    /// <summary>The occupancy on file for the parent's poles; a pole that is missing has none.</summary>
    public required IReadOnlyCollection<PoleOccupancy> Occupancies { get; init; }

    /// <summary>Ids of the orders that already are children of the parent.</summary>
    public required IReadOnlyCollection<string> ExistingChildOrderIds { get; init; }

    /// <summary>The user dispatching: the creator of every child and the author of its dispatch.</summary>
    public required string DispatchedBy { get; init; }

    /// <summary>What kind of actor <see cref="DispatchedBy"/> is.</summary>
    public ActorKind ActorKind { get; init; } = ActorKind.Human;
}
