namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>Distribuidoras</c> table.</summary>
internal sealed class DistribuidoraRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
}
