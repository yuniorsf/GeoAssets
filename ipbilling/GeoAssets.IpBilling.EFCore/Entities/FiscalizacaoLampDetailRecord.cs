using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>FiscalizacaoLampDetails</c> table.</summary>
internal sealed class FiscalizacaoLampDetailRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LineId { get; set; }

    public FiscalizacaoOperationGroup OperationGroup { get; set; }

    public LampTypeCode Code { get; set; }

    public int WattageW { get; set; }

    public int Quantity { get; set; }
}
