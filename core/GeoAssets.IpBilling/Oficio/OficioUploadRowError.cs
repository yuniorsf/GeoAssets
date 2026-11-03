namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// One structured row-level error from an Ofício spreadsheet upload — sheet row number (1-based,
/// matching what a spreadsheet application shows), the offending column's name, and a
/// human-readable message. Mirrors <c>Fiscalizacao.FiscalizacaoUploadRowError</c>'s shape
/// (XD01-183/186).
/// </summary>
public sealed record OficioUploadRowError(int RowNumber, string Column, string Message);
