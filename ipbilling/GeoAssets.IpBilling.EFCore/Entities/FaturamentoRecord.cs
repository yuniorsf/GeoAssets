namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>Faturamentos</c> table.</summary>
internal sealed class FaturamentoRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MunicipioId { get; set; }

    public string Competencia { get; set; } = string.Empty;

    public decimal KwhFaturado { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
