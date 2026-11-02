using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// One parsed row of an uploaded Levantamento spreadsheet — one lamp-type/wattage aggregate
/// count for one transformer. Deliberately shaped as plain primitives rather than referencing
/// any Excel-specific type: the actual <c>GeoAssets.Provider.Xlsx</c>/ClosedXML parsing is
/// XD01-180's own deliverable (the REST/upload layer); this ticket's
/// <see cref="LevantamentoWorkbookMapper"/> only needs rows already reduced to this shape.
/// </summary>
public sealed record LevantamentoWorkbookRow(
    string CodigoInstalacao,
    LampTypeCode LampTypeCode,
    int WattageW,
    int Quantity);
