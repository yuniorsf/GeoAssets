using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>LampTypes</c> table.</summary>
internal sealed class LampTypeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public LampTypeCode Code { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<LampWattageOptionRecord> WattageOptions { get; set; } = [];
}
