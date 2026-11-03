using System.Globalization;

namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Parses the raw cell rows <c>IWorkbookReader.ReadRows</c> (<c>GeoAssets.Provider.Xlsx</c>)
/// returns into <see cref="OficioWorkbookRow"/>s, collecting every malformed row as a structured
/// <see cref="OficioUploadRowError"/> instead of failing on the first one — same convention as
/// <c>Fiscalizacao.FiscalizacaoWorkbookRowParser</c> (XD01-183/186).
///
/// Deliberately has no dependency on <c>GeoAssets.Provider.Xlsx</c>/ClosedXML itself — it only
/// needs the already-extracted raw cell text, so it's independently unit-testable without a
/// real .xlsx file.
///
/// <strong>Column layout is this implementation's own invention</strong>, not sourced from a
/// real legacy GEOS_FAT_IP spreadsheet template (none is available in this repository/
/// environment): row 1 is a header (skipped); column A = Código de Instalação, B = Grupo de
/// Operação (the numeric <see cref="OficioOperationGroup"/> value, 1-4, matching that enum's own
/// doc comment), C = Quantidade. Confirm against the real legacy template before treating this
/// as ground truth. The Prefeitura-assigned <c>NumeroPrefeitura</c> is deliberately NOT a column
/// here — it is document-level metadata for the whole upload, not a per-row value, so the REST
/// layer (<c>IpBillingOficioRestApiExtensions</c>) takes it as a separate request parameter.
/// </summary>
public static class OficioWorkbookRowParser
{
    public static OficioWorkbookParseResult Parse(IReadOnlyList<IReadOnlyList<string?>> rawRows)
    {
        ArgumentNullException.ThrowIfNull(rawRows);

        var rows = new List<OficioWorkbookRow>();
        var errors = new List<OficioUploadRowError>();

        for (var i = 1; i < rawRows.Count; i++)
        {
            ParseRow(rawRows[i], rowNumber: i + 1, rows, errors);
        }

        return new OficioWorkbookParseResult(rows, errors);
    }

    private static void ParseRow(
        IReadOnlyList<string?> raw, int rowNumber, List<OficioWorkbookRow> rows, List<OficioUploadRowError> errors)
    {
        var codigo = Cell(raw, 0);
        if (string.IsNullOrWhiteSpace(codigo))
        {
            errors.Add(new OficioUploadRowError(rowNumber, "Código de Instalação", "Required."));
            return;
        }

        if (!TryParseOperationGroup(Cell(raw, 1), out var operationGroup))
        {
            errors.Add(new OficioUploadRowError(rowNumber, "Grupo de Operação", $"'{Cell(raw, 1)}' is not a recognized operation group (expected 1-4)."));
            return;
        }

        if (!TryParsePositiveInt(Cell(raw, 2), out var quantity))
        {
            errors.Add(new OficioUploadRowError(rowNumber, "Quantidade", $"'{Cell(raw, 2)}' is not a valid quantity."));
            return;
        }

        rows.Add(new OficioWorkbookRow(codigo, operationGroup, quantity));
    }

    private static string? Cell(IReadOnlyList<string?> row, int index) => index < row.Count ? row[index] : null;

    private static bool TryParseOperationGroup(string? raw, out OficioOperationGroup group)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) &&
            Enum.IsDefined(typeof(OficioOperationGroup), numeric) &&
            numeric != (int)OficioOperationGroup.Unspecified)
        {
            group = (OficioOperationGroup)numeric;
            return true;
        }

        group = OficioOperationGroup.Unspecified;
        return false;
    }

    private static bool TryParsePositiveInt(string? raw, out int value) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
}
