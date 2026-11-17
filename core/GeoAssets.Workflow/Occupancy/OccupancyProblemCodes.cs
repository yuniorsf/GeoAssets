namespace GeoAssets.Workflow.Occupancy;

/// <summary>
/// The stable <c>code</c> values of the staged-occupancy routes (XD01-315), beside the ones they
/// share with the order-target routes (<see cref="Targets.WorkflowProblemCodes"/>). The first
/// two also appear in <see cref="TargetOccupancyView.Warnings"/>, where they describe a staged
/// set that no longer fits the pole rather than a refused request.
/// </summary>
public static class OccupancyProblemCodes
{
    /// <summary>A change is malformed, or does not apply to the pole (an occupied slot, an illegal status move, an invalid entry).</summary>
    public const string InvalidChange = "occupancy.invalid-change";

    /// <summary>A change addresses a slot holding nothing, or an entry that is not on the pole.</summary>
    public const string EntryNotFound = "occupancy.entry-not-found";

    /// <summary>
    /// The pole's occupancy changed after the set was staged. Only ever a warning on a read:
    /// staging again simply records the pole's current version.
    /// </summary>
    public const string StaleBaseline = "occupancy.stale-baseline";

    /// <summary>The target's feature is not of a pole asset type.</summary>
    public const string NotAPole = "occupancy.not-a-pole";

    /// <summary>The target was released when its order closed.</summary>
    public const string TargetReleased = "occupancy.target-released";

    /// <summary>
    /// An approval could not commit: a pole's occupancy, or a staged set, changed after the set
    /// was staged (XD01-318). Nothing was committed; the changes must be staged again.
    /// </summary>
    public const string ConcurrentModification = "occupancy.concurrent-modification";
}
