namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>IpTransformerIndexes</c> table.</summary>
internal sealed class IpTransformerIndexRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string MunicipioIbgeCode { get; set; } = string.Empty;

    public string CodigoInstalacao { get; set; } = string.Empty;

    public Guid TransformerFeatureId { get; set; }
}
