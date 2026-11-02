using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>LevantamentoLampDetails</c> table.</summary>
internal sealed class LevantamentoLampDetailRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TransformerLineId { get; set; }

    public LampTypeCode Code { get; set; }

    public int WattageW { get; set; }

    public int Quantity { get; set; }
}
