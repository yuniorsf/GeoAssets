namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>OficioLines</c> table.</summary>
internal sealed class OficioLineRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ServiceOrderId { get; set; } = string.Empty;

    public string CodigoInstalacao { get; set; } = string.Empty;

    public int Agregado { get; set; }

    public int Retroativo { get; set; }

    public List<OficioOperationGroupDetailRecord> OperationGroups { get; init; } = [];
}
