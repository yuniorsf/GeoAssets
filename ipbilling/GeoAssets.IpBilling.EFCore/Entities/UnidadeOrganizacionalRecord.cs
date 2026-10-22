namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>UnidadesOrganizacionais</c> table.</summary>
internal sealed class UnidadeOrganizacionalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MunicipioId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }
}
