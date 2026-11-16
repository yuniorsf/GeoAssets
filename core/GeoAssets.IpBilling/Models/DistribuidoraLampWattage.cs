namespace GeoAssets.IpBilling.Models;

/// <summary>
/// One row of a <see cref="Distribuidora"/>'s own lamp wattage chart (XD01-301): the wattage a
/// lamp type is billed at for that distributor, with its ballast-loss figure. Same shape as the
/// global standard chart's <see cref="LampWattageOption"/>, but owned by one Distribuidora and
/// user-editable. Unique per <c>(DistribuidoraId, Code, WattageW)</c>.
/// </summary>
public sealed class DistribuidoraLampWattage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required Guid DistribuidoraId { get; set; }

    public required LampTypeCode Code { get; set; }

    /// <summary>Nominal lamp wattage, in watts. Always positive.</summary>
    public required int WattageW { get; set; }

    /// <summary>Additional ballast/reactor loss, in watts. Never negative.</summary>
    public decimal BallastLossW { get; set; }

    /// <summary><see cref="WattageW"/> + <see cref="BallastLossW"/> — the figure actually billed.</summary>
    public decimal TotalBilledW => WattageW + BallastLossW;
}
