namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>LampWattageOptions</c> table.</summary>
internal sealed class LampWattageOptionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LampTypeId { get; set; }
    public LampTypeRecord? LampType { get; set; }

    public int WattageW { get; set; }

    public decimal BallastLossW { get; set; }
}
