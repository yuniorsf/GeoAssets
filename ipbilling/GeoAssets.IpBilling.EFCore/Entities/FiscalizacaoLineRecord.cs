namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>FiscalizacaoLines</c> table.</summary>
internal sealed class FiscalizacaoLineRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ServiceOrderId { get; set; } = string.Empty;

    public string CodigoInstalacao { get; set; } = string.Empty;

    public int Agregado { get; set; }

    public int Retroativo { get; set; }

    public List<FiscalizacaoOperationGroupDetailRecord> OperationGroups { get; init; } = [];
}
