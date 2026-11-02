using System.Globalization;
using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Parses the raw cell rows <c>IWorkbookReader.ReadRows</c> (<c>GeoAssets.Provider.Xlsx</c>)
/// returns into <see cref="LevantamentoWorkbookRow"/>s, collecting every malformed row as a
/// structured <see cref="LevantamentoUploadRowError"/> instead of failing on the first one —
/// per this ticket's "reports all ... together, not one at a time" convention (XD01-179/180).
///
/// Deliberately has no dependency on <c>GeoAssets.Provider.Xlsx</c>/ClosedXML itself — it only
/// needs the already-extracted raw cell text, so it's independently unit-testable without a
/// real .xlsx file.
///
/// <strong>Column layout is this implementation's own invention</strong>, not sourced from a
/// real legacy GEOS_FAT_IP spreadsheet template (none is available in this repository/
/// environment): row 1 is a header (skipped); column A = Código de Instalação, B = Tipo de
/// Lâmpada (a <see cref="LampTypeCode"/> name), C = Potência (W), D = Quantidade. Confirm
/// against the real legacy template before treating this as ground truth.
/// </summary>
public static class LevantamentoWorkbookRowParser
{
    public static LevantamentoWorkbookParseResult Parse(IReadOnlyList<IReadOnlyList<string?>> rawRows)
    {
        ArgumentNullException.ThrowIfNull(rawRows);

        var rows = new List<LevantamentoWorkbookRow>();
        var errors = new List<LevantamentoUploadRowError>();

        for (var i = 1; i < rawRows.Count; i++)
        {
            ParseRow(rawRows[i], rowNumber: i + 1, rows, errors);
        }

        return new LevantamentoWorkbookParseResult(rows, errors);
    }

    private static void ParseRow(
        IReadOnlyList<string?> raw, int rowNumber, List<LevantamentoWorkbookRow> rows, List<LevantamentoUploadRowError> errors)
    {
        var codigo = Cell(raw, 0);
        if (string.IsNullOrWhiteSpace(codigo))
        {
            errors.Add(new LevantamentoUploadRowError(rowNumber, "Código de Instalação", "Required."));
            return;
        }

        var rawLampType = Cell(raw, 1);
        if (!Enum.TryParse<LampTypeCode>(rawLampType, ignoreCase: true, out var lampType))
        {
            errors.Add(new LevantamentoUploadRowError(rowNumber, "Tipo de Lâmpada", $"'{rawLampType}' is not a recognized lamp type."));
            return;
        }

        if (!TryParsePositiveInt(Cell(raw, 2), out var wattageW))
        {
            errors.Add(new LevantamentoUploadRowError(rowNumber, "Potência (W)", $"'{Cell(raw, 2)}' is not a valid wattage."));
            return;
        }

        if (!TryParsePositiveInt(Cell(raw, 3), out var quantity))
        {
            errors.Add(new LevantamentoUploadRowError(rowNumber, "Quantidade", $"'{Cell(raw, 3)}' is not a valid quantity."));
            return;
        }

        rows.Add(new LevantamentoWorkbookRow(codigo, lampType, wattageW, quantity));
    }

    private static string? Cell(IReadOnlyList<string?> row, int index) => index < row.Count ? row[index] : null;

    private static bool TryParsePositiveInt(string? raw, out int value) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
}
