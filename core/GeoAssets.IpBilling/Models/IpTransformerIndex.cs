namespace GeoAssets.IpBilling.Models;

/// <summary>
/// Lookup row mapping a legacy (município, código de instalação) pair — the identifiers
/// every uploaded spreadsheet row carries — to the real <c>GeoFeature</c> standing in for
/// that transformer on the map (the "Transformador (IP)" <c>AssetType</c>, see
/// <see cref="AssetTypes.IpBillingAssetTypes.Transformador"/>).
///
/// Consulted by <see cref="Resolution.ITransformerResolver"/> (concrete implementation lands
/// with Phase 1, XD01-179) instead of every calculation engine re-deriving the mapping itself.
/// </summary>
public sealed class IpTransformerIndex
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>7-digit IBGE code of the owning <see cref="Municipio"/>.</summary>
    public required string MunicipioIbgeCode { get; set; }

    /// <summary>Legacy installation code ("código de instalação") as it appears in uploaded spreadsheets.</summary>
    public required string CodigoInstalacao { get; set; }

    /// <summary>The <c>GeoFeature.Id</c> of the "Transformador (IP)" feature this row resolves to.</summary>
    public required Guid TransformerFeatureId { get; set; }
}
