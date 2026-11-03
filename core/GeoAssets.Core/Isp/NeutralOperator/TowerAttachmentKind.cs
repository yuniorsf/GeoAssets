namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>
/// Attachment-equipment catalog for a wireless monopole/lattice tower (XD01-266) — structurally
/// distinct from a <see cref="PoleAttachmentSlot"/>'s telecom-cable occupancy, since a tower
/// carries discrete mounted equipment rather than cables sharing a fixed 500mm band.
/// </summary>
public enum TowerAttachmentKind
{
    Bracket,
    Radome,
    Dish,
    PowerMeter,
}
