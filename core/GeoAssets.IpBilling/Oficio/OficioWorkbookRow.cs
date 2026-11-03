namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// One parsed row of an uploaded Ofício spreadsheet — one operation-group count for one
/// installation. Mirrors <c>Fiscalizacao.FiscalizacaoWorkbookRow</c>: the actual Excel-parsing
/// layer is a separate follow-on ticket's deliverable (XD01-186, same split as XD01-182/183);
/// this ticket's <see cref="OficioWorkbookMapper"/> only needs rows already reduced to this shape.
/// </summary>
public sealed record OficioWorkbookRow(
    string CodigoInstalacao,
    OficioOperationGroup OperationGroup,
    int Count);
