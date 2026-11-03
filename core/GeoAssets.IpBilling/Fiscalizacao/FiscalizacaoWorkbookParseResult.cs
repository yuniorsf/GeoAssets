namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>Result of <see cref="FiscalizacaoWorkbookRowParser.Parse"/>.</summary>
public sealed record FiscalizacaoWorkbookParseResult(
    IReadOnlyList<FiscalizacaoWorkbookRow> Rows,
    IReadOnlyList<FiscalizacaoUploadRowError> Errors);
