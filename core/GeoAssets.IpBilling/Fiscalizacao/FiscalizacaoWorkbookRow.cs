namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// One parsed row of an uploaded Fiscalização spreadsheet — one operation-group count for one
/// installation. Deliberately shaped as plain primitives rather than referencing any
/// Excel-specific type, mirroring <c>Levantamento.LevantamentoWorkbookRow</c>: the actual
/// <c>GeoAssets.Provider.Xlsx</c>/ClosedXML parsing is XD01-183's own deliverable (the
/// REST/upload layer); this ticket's <see cref="FiscalizacaoWorkbookMapper"/> only needs rows
/// already reduced to this shape.
/// </summary>
public sealed record FiscalizacaoWorkbookRow(
    string CodigoInstalacao,
    FiscalizacaoOperationGroup OperationGroup,
    int Count);
