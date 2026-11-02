using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>Levantamento24hLines</c> table.</summary>
internal sealed class Levantamento24hLineRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ServiceOrderId { get; set; } = string.Empty;

    public Guid FeatureId { get; set; }

    public LampTypeCode Code { get; set; }

    public int WattageW { get; set; }

    public int Quantity { get; set; }

    public decimal Kwh { get; set; }
}
