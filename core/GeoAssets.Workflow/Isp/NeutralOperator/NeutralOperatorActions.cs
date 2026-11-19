using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The neutral-operator verbs (XD01-366, ADR-001 smell D) — declared by the vertical, not the engine. Their codes are the
/// ones the closed enumeration used, so stored action-log rows, permissions and recipient actions keep their numbers
/// (<see cref="OrderActionAliases"/>).
/// </summary>
public static class NeutralOperatorActions
{
    /// <summary>Flag a pole attachment as an undeclared occupant (Active → CaboARevelia, XD01-263).</summary>
    public static OrderAction FlagClandestine { get; } = new("FlagClandestine");

    /// <summary>Contest a clandestine flag with evidence (CaboARevelia → Disputed, XD01-269).</summary>
    public static OrderAction Dispute { get; } = new("Dispute");
}
