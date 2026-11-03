namespace GeoAssets.IpBilling.Oficio;

/// <summary>Result of <see cref="OficioWorkbookRowParser.Parse"/>.</summary>
public sealed record OficioWorkbookParseResult(
    IReadOnlyList<OficioWorkbookRow> Rows,
    IReadOnlyList<OficioUploadRowError> Errors);
