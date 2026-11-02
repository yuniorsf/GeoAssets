namespace GeoAssets.IpBilling.Levantamento;

/// <summary>Result of <see cref="LevantamentoWorkbookRowParser.Parse"/>.</summary>
public sealed record LevantamentoWorkbookParseResult(
    IReadOnlyList<LevantamentoWorkbookRow> Rows,
    IReadOnlyList<LevantamentoUploadRowError> Errors);
