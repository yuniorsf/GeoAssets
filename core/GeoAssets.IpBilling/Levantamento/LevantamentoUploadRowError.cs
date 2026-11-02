namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// One structured row-level error from a Levantamento spreadsheet upload — sheet row number
/// (1-based, matching what a spreadsheet application shows), the offending column's name, and
/// a human-readable message. Mirrors <c>ServiceOrderAttributeValidationException</c>'s
/// "structured, not a generic failure" shape, per this ticket's own description (XD01-180).
/// </summary>
public sealed record LevantamentoUploadRowError(int RowNumber, string Column, string Message);
