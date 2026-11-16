namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// One parsed row of an uploaded Ofício spreadsheet — one operation-group count for one
/// installation. Mirrors <c>Fiscalizacao.FiscalizacaoWorkbookRow</c>: the actual Excel-parsing
/// layer is a separate follow-on ticket's deliverable (XD01-186, same split as XD01-182/183);
/// this ticket's <see cref="OficioWorkbookMapper"/> only needs rows already reduced to this shape.
/// </summary>
/// <param name="CodigoInstalacao">Legacy installation code.</param>
/// <param name="OperationGroup">The operation group the count is reported against.</param>
/// <param name="Count">Number of points.</param>
/// <param name="LampCode">Optional lamp type of these points (XD01-301); present exactly when
/// <paramref name="WattageW"/> is.</param>
/// <param name="WattageW">Optional nominal lamp wattage, in watts.</param>
public sealed record OficioWorkbookRow(
    string CodigoInstalacao,
    OficioOperationGroup OperationGroup,
    int Count,
    Models.LampTypeCode? LampCode = null,
    int? WattageW = null);
