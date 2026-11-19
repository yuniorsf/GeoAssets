using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Workflow;

/// <summary>
/// The IP-billing (GEOS_FAT_IP) verbs (XD01-366, ADR-001 smell D) — declared by the vertical, not the engine. Their codes
/// are the ones the closed enumeration used, so stored action-log rows and transitions keep their numbers
/// (<see cref="OrderActionAliases"/>).
/// </summary>
public static class IpBillingActions
{
    /// <summary>Run the calculation engine over the order's uploaded data (Carregado → Calculado, XD01-176).</summary>
    public static OrderAction Calculate { get; } = new("Calculate");

    /// <summary>Confirm a calculated order's figures as final (Calculado → Validado, XD01-176).</summary>
    public static OrderAction Validate { get; } = new("Validate");
}
