namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// One structured row-level error from a Fiscalização spreadsheet upload — sheet row number
/// (1-based, matching what a spreadsheet application shows), the offending column's name, and
/// a human-readable message. Mirrors <c>Levantamento.LevantamentoUploadRowError</c>'s shape
/// (XD01-180/183).
/// </summary>
public sealed record FiscalizacaoUploadRowError(int RowNumber, string Column, string Message);
