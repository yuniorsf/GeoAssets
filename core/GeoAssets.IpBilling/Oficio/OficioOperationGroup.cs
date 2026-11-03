namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// The four operation categories an Ofício spreadsheet row can report a point count against,
/// for one "código de instalação" line. Numeric values match the legacy GEOS_FAT_IP column
/// order this ticket's calculation formulas are defined in terms of (XD01-185) — groups 1/2 are
/// Ofício-specific (inclusão/retirada, unlike Fiscalização's current-count/prior-count pair),
/// groups 3/4 reuse Fiscalização's "novos 24h irregulares"/"regularização 24h" labels exactly.
/// </summary>
public enum OficioOperationGroup
{
    /// <summary>Sentinel default — never a valid operation group on a persisted row (CA1008).</summary>
    Unspecified = 0,

    /// <summary>New installation included this month ("inclusão").</summary>
    Inclusao = 1,

    /// <summary>Installation removed this month ("retirada").</summary>
    Retirada = 2,

    /// <summary>Newly found 24h-irregular points ("novos 24h irregulares").</summary>
    Novos24hIrregulares = 3,

    /// <summary>24h-irregular points regularized back to normal ("regularização 24h").</summary>
    Regularizacao24h = 4,
}
