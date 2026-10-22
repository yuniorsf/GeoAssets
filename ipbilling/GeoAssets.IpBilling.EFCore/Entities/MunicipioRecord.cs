using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>Municipios</c> table.</summary>
internal sealed class MunicipioRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string IbgeCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Uf { get; set; } = string.Empty;

    public Guid DistribuidoraOrganizationId { get; set; }

    public Guid? PrefeituraOrganizationId { get; set; }

    public OrigemWorkflow OrigemLevantamento   { get; set; }
    public OrigemWorkflow OrigemFiscalizacao   { get; set; }
    public OrigemWorkflow OrigemOficio         { get; set; }
    public OrigemWorkflow OrigemMemoriaCalculo { get; set; }
}
