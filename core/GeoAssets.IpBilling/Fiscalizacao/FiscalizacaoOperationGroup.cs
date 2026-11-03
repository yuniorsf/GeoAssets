namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// The four operation categories a Fiscalização spreadsheet row can report a point count
/// against, for one "código de instalação" line. Numeric values match the legacy GEOS_FAT_IP
/// column order this ticket's calculation formulas are defined in terms of (XD01-182).
/// </summary>
public enum FiscalizacaoOperationGroup
{
    /// <summary>Sentinel default — never a valid operation group on a persisted row (CA1008).</summary>
    Unspecified = 0,

    /// <summary>Current spot-check count ("Fiscalização").</summary>
    Fiscalizacao = 1,

    /// <summary>
    /// Inspector-recalled prior count ("Último Levantamento") — deliberately <em>not</em>
    /// cross-checked against the real previous Levantamento survey; confirmed intentional
    /// legacy behavior, kept as-is.
    /// </summary>
    UltimoLevantamento = 2,

    /// <summary>Newly found 24h-irregular points ("novos 24h irregulares").</summary>
    Novos24hIrregulares = 3,

    /// <summary>24h-irregular points regularized back to normal ("regularização 24h").</summary>
    Regularizacao24h = 4,
}
