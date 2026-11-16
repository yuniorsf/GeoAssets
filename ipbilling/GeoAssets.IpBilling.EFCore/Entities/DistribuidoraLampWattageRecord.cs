using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>DistribuidoraLampWattages</c> table.</summary>
internal sealed class DistribuidoraLampWattageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DistribuidoraId { get; set; }

    public LampTypeCode Code { get; set; }

    public int WattageW { get; set; }

    public decimal BallastLossW { get; set; }
}
