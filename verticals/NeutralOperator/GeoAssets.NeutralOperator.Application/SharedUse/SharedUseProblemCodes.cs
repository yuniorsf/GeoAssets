namespace GeoAssets.NeutralOperator.Application.SharedUse;

/// <summary>
/// The stable <c>code</c> values of the shared-use dispatch problems (XD01-316), beside the ones
/// in <see cref="Targets.WorkflowProblemCodes"/>. Shared by the host that answers with them and
/// the client that turns them back into exceptions.
/// </summary>
public static class SharedUseProblemCodes
{
    /// <summary>The order's type declares no fan-out.</summary>
    public const string NoFanOut = "orders.no-fan-out";

    /// <summary>The order has no target.</summary>
    public const string NoTargets = "dispatch.no-targets";

    /// <summary>At least one target that is not blocked has no activity.</summary>
    public const string TargetWithoutActivity = "dispatch.target-without-activity";

    /// <summary>Every target is blocked by another open order.</summary>
    public const string AllTargetsBlocked = "dispatch.all-targets-blocked";

    /// <summary>No pole has an occupant with an active contract and a known organization.</summary>
    public const string NoOccupants = "dispatch.no-occupants";

    /// <summary>The code of <paramref name="reason"/>.</summary>
    public static string For(SharedUseDispatchBlockReason reason) => reason switch
    {
        SharedUseDispatchBlockReason.NoTargets => NoTargets,
        SharedUseDispatchBlockReason.TargetsWithoutActivity => TargetWithoutActivity,
        SharedUseDispatchBlockReason.AllTargetsBlocked => AllTargetsBlocked,
        SharedUseDispatchBlockReason.NoEligibleOccupants => NoOccupants,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a defined block reason."),
    };

    /// <summary>The block reason <paramref name="code"/> stands for; null when it is not one of the dispatch codes.</summary>
    public static SharedUseDispatchBlockReason? ReasonOf(string? code) => code switch
    {
        NoTargets => SharedUseDispatchBlockReason.NoTargets,
        TargetWithoutActivity => SharedUseDispatchBlockReason.TargetsWithoutActivity,
        AllTargetsBlocked => SharedUseDispatchBlockReason.AllTargetsBlocked,
        NoOccupants => SharedUseDispatchBlockReason.NoEligibleOccupants,
        _ => null,
    };
}
