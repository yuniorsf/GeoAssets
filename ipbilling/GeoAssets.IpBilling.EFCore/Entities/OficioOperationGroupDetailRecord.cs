using GeoAssets.IpBilling.Oficio;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>OficioOperationGroupDetails</c> table.</summary>
internal sealed class OficioOperationGroupDetailRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LineId { get; set; }

    public OficioOperationGroup OperationGroup { get; set; }

    public int Count { get; set; }
}
