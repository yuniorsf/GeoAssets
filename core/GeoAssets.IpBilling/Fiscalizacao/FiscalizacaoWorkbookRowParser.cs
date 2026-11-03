using System.Globalization;

namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Parses the raw cell rows <c>IWorkbookReader.ReadRows</c> (<c>GeoAssets.Provider.Xlsx</c>)
/// returns into <see cref="FiscalizacaoWorkbookRow"/>s, collecting every malformed row as a
/// structured <see cref="FiscalizacaoUploadRowError"/> instead of failing on the first one —
/// same "reports all together, not one at a time" convention as
/// <c>Levantamento.LevantamentoWorkbookRowParser</c> (XD01-179/180/183).
///
/// Deliberately has no dependency on <c>GeoAssets.Provider.Xlsx</c>/ClosedXML itself — it only
/// needs the already-extracted raw cell text, so it's independently unit-testable without a
/// real .xlsx file.
///
/// <strong>Column layout is this implementation's own invention</strong>, not sourced from a
/// real legacy GEOS_FAT_IP spreadsheet template (none is available in this repository/
/// environment): row 1 is a header (skipped); column A = Código de Instalação, B = Grupo de
/// Operação (the numeric <see cref="FiscalizacaoOperationGroup"/> value, 1-4, matching the
/// legacy column order that enum's own doc comment describes), C = Quantidade. Confirm against
/// the real legacy template before treating this as ground truth.
/// </summary>
public static class FiscalizacaoWorkbookRowParser
{
    public static FiscalizacaoWorkbookParseResult Parse(IReadOnlyList<IReadOnlyList<string?>> rawRows)
    {
        ArgumentNullException.ThrowIfNull(rawRows);

        var rows = new List<FiscalizacaoWorkbookRow>();
        var errors = new List<FiscalizacaoUploadRowError>();

        for (var i = 1; i < rawRows.Count; i++)
        {
            ParseRow(rawRows[i], rowNumber: i + 1, rows, errors);
        }

        return new FiscalizacaoWorkbookParseResult(rows, errors);
    }

    private static void ParseRow(
        IReadOnlyList<string?> raw, int rowNumber, List<FiscalizacaoWorkbookRow> rows, List<FiscalizacaoUploadRowError> errors)
    {
        var codigo = Cell(raw, 0);
        if (string.IsNullOrWhiteSpace(codigo))
        {
            errors.Add(new FiscalizacaoUploadRowError(rowNumber, "Código de Instalação", "Required."));
            return;
        }

        if (!TryParseOperationGroup(Cell(raw, 1), out var operationGroup))
        {
            errors.Add(new FiscalizacaoUploadRowError(rowNumber, "Grupo de Operação", $"'{Cell(raw, 1)}' is not a recognized operation group (expected 1-4)."));
            return;
        }

        if (!TryParsePositiveInt(Cell(raw, 2), out var quantity))
        {
            errors.Add(new FiscalizacaoUploadRowError(rowNumber, "Quantidade", $"'{Cell(raw, 2)}' is not a valid quantity."));
            return;
        }

        rows.Add(new FiscalizacaoWorkbookRow(codigo, operationGroup, quantity));
    }

    private static string? Cell(IReadOnlyList<string?> row, int index) => index < row.Count ? row[index] : null;

    private static bool TryParseOperationGroup(string? raw, out FiscalizacaoOperationGroup group)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) &&
            Enum.IsDefined(typeof(FiscalizacaoOperationGroup), numeric) &&
            numeric != (int)FiscalizacaoOperationGroup.Unspecified)
        {
            group = (FiscalizacaoOperationGroup)numeric;
            return true;
        }

        group = FiscalizacaoOperationGroup.Unspecified;
        return false;
    }

    private static bool TryParsePositiveInt(string? raw, out int value) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
}
