namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>OficioHeaders</c> table.</summary>
internal sealed class OficioHeaderRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ServiceOrderId { get; set; } = string.Empty;

    public Guid MunicipioId { get; set; }

    public string Competencia { get; set; } = string.Empty;

    public string? NumeroPrefeitura { get; set; }

    public bool IsValidated { get; set; }
}
