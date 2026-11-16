using GeoAssets.IpBilling.Oficio;
using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>OficioLampDetails</c> table.</summary>
internal sealed class OficioLampDetailRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LineId { get; set; }

    public OficioOperationGroup OperationGroup { get; set; }

    public LampTypeCode Code { get; set; }

    public int WattageW { get; set; }

    public int Quantity { get; set; }
}
