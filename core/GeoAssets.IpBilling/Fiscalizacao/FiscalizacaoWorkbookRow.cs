namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// One parsed row of an uploaded Fiscalização spreadsheet — one operation-group count for one
/// installation. Deliberately shaped as plain primitives rather than referencing any
/// Excel-specific type, mirroring <c>Levantamento.LevantamentoWorkbookRow</c>: the actual
/// <c>GeoAssets.Provider.Xlsx</c>/ClosedXML parsing is XD01-183's own deliverable (the
/// REST/upload layer); this ticket's <see cref="FiscalizacaoWorkbookMapper"/> only needs rows
/// already reduced to this shape.
/// </summary>
/// <param name="CodigoInstalacao">Legacy installation code.</param>
/// <param name="OperationGroup">The operation group the count is reported against.</param>
/// <param name="Count">Number of points.</param>
/// <param name="LampCode">Optional lamp type of these points (XD01-301); present exactly when
/// <paramref name="WattageW"/> is.</param>
/// <param name="WattageW">Optional nominal lamp wattage, in watts.</param>
public sealed record FiscalizacaoWorkbookRow(
    string CodigoInstalacao,
    FiscalizacaoOperationGroup OperationGroup,
    int Count,
    Models.LampTypeCode? LampCode = null,
    int? WattageW = null);
