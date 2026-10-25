namespace GeoAssets.IpBilling.Models;

/// <summary>
/// One row of the public-lighting lamp-type reference catalog (XD01-176). Seeded once via
/// an EF Core migration (see <c>GeoAssets.IpBilling.Persistence</c>'s initial-create migration)
/// and never user-editable — mirrors <c>AssetType.Point</c>/<c>Line</c>/<c>Area</c>'s
/// "global built-in, not created through the UI" convention, but for billing reference data
/// instead of map styling.
/// </summary>
public sealed class LampType
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required LampTypeCode Code { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The wattages this lamp type is billed at, each with its own ballast-loss figure.
    /// Populated when loaded through <c>ILampTypeRepository</c>; empty by default.
    /// </summary>
    public List<LampWattageOption> WattageOptions { get; init; } = [];
}
