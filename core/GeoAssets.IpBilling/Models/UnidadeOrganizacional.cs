namespace GeoAssets.IpBilling.Models;

/// <summary>
/// A reporting/grouping subdivision of a <see cref="Municipio"/> (e.g. a district or
/// administrative zone used to group transformers/poles on reports). Reference data only —
/// unlike <see cref="Municipio"/>, this is **not** an authorization boundary; no grant or
/// permission check is ever scoped to a <see cref="UnidadeOrganizacional"/>.
/// </summary>
public sealed class UnidadeOrganizacional
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required Guid MunicipioId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional short reporting code, e.g. a legacy district code.</summary>
    public string? Code { get; set; }
}
