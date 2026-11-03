using GeoAssets.IpBilling.Fiscalizacao;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>FiscalizacaoOperationGroupDetails</c> table.</summary>
internal sealed class FiscalizacaoOperationGroupDetailRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LineId { get; set; }

    public FiscalizacaoOperationGroup OperationGroup { get; set; }

    public int Count { get; set; }
}
