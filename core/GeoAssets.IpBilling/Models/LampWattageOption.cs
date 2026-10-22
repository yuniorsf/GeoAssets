namespace GeoAssets.IpBilling.Models;

/// <summary>
/// One allowed wattage for a <see cref="LampType"/>, carrying the extra ballast-loss wattage
/// billed alongside the lamp's own nominal consumption for discharge-lamp technologies.
///
/// <see cref="BallastLossW"/> is nonzero only for <see cref="LampTypeCode.MT"/>,
/// <see cref="LampTypeCode.MV"/>, <see cref="LampTypeCode.VM"/>, and
/// <see cref="LampTypeCode.VS"/> — the discharge-lamp types that require an external
/// ballast/reactor — per this ticket's deliverable description. LED/fluorescent/
/// incandescent/halogen entries always carry <c>0</c>.
/// </summary>
public sealed class LampWattageOption
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LampTypeId { get; set; }

    /// <summary>Nominal lamp wattage, in watts.</summary>
    public int WattageW { get; set; }

    /// <summary>Additional ballast/reactor loss, in watts. Zero for non-discharge lamp types.</summary>
    public decimal BallastLossW { get; set; }

    /// <summary><see cref="WattageW"/> + <see cref="BallastLossW"/> — the figure actually billed.</summary>
    public decimal TotalBilledW => WattageW + BallastLossW;
}
