namespace GeoAssets.IpBilling.Models;

/// <summary>
/// An electricity distributor (utility) — the owner of a lamp wattage chart
/// (<see cref="DistribuidoraLampWattage"/>) that the Fiscalização/Ofício point-to-kWh conversion
/// reads before falling back to the global standard chart (<see cref="LampType"/>/
/// <see cref="LampWattageOption"/>) (XD01-301). A <see cref="Municipio"/> belongs to at most one
/// Distribuidora via <see cref="Municipio.DistribuidoraId"/>.
///
/// Distinct from <see cref="Municipio.DistribuidoraOrganizationId"/>: that is the tenancy/
/// authorization link to an <c>Organization</c>; this entity is billing reference data.
/// <see cref="OrganizationId"/> ties the two together, mirroring how <see cref="Municipio"/>
/// references its owning Organization by id rather than by navigation.
/// </summary>
public sealed class Distribuidora
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The utility <c>Organization</c> this Distribuidora's reference data belongs to.</summary>
    public required Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
}
