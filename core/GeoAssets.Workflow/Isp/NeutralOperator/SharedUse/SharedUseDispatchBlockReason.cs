namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// Why a shared-use order cannot be dispatched as it stands. Listed in the order the conditions
/// are checked: the first one that holds is the one reported.
/// </summary>
public enum SharedUseDispatchBlockReason
{
    /// <summary>The order has no target.</summary>
    NoTargets = 0,

    /// <summary>At least one target that is not blocked has no activity assigned.</summary>
    TargetsWithoutActivity = 1,

    /// <summary>Every target is blocked by another open order holding the same pole.</summary>
    AllTargetsBlocked = 2,

    /// <summary>No pole of the order has an occupant with an active contract and a known organization.</summary>
    NoEligibleOccupants = 3,
}
