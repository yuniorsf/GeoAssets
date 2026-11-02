using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// A snapshot of one standing 24h-irregular lamp-group <c>GeoFeature</c> (the
/// <c>LampGroup24h</c> <c>AssetType</c>) that fed into a specific Levantamento's calculation —
/// persisted for audit, unlike the generic <see cref="Calculation.IpBillingCalculationService"/>'s
/// live, re-derived-every-time sum. 24h-lamp kWh (12h08 daily rate) is never retroactive, per
/// this ticket's own description.
/// </summary>
public sealed class Levantamento24hLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the owning <see cref="LevantamentoHeader.ServiceOrderId"/>.</summary>
    public required string ServiceOrderId { get; set; }

    /// <summary>The source <c>LampGroup24h</c> <c>GeoFeature.Id</c>.</summary>
    public required Guid FeatureId { get; set; }

    public required LampTypeCode Code { get; set; }

    public required int WattageW { get; set; }

    public required int Quantity { get; set; }

    /// <summary>kWh billed for this group at the 12h08 daily rate for the competência.</summary>
    public decimal Kwh { get; set; }
}
