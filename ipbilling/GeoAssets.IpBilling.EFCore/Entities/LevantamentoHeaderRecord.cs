namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>LevantamentoHeaders</c> table.</summary>
internal sealed class LevantamentoHeaderRecord
{
    public string ServiceOrderId { get; set; } = string.Empty;

    public Guid MunicipioId { get; set; }

    public string Competencia { get; set; } = string.Empty;

    public bool IsValidated { get; set; }
}
