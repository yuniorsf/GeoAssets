using System.Globalization;

namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Parses the raw cell rows <c>IWorkbookReader.ReadRows</c> (<c>GeoAssets.Core.Interfaces</c>)
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
/// legacy column order that enum's own doc comment describes), C = Quantidade, plus two
/// OPTIONAL columns added by XD01-301: D = Tipo de Lâmpada (a <see cref="Models.LampTypeCode"/>
/// name, e.g. <c>VS</c>, case-insensitive) and E = Potência (W) (a positive integer). D and E
/// must be both filled or both blank on a row; a spreadsheet without them parses exactly as
/// before. With D/E filled, C is the quantity of that lamp type/wattage. Confirm against
/// the real legacy template before treating this as ground truth.
/// </summary>
public static class FiscalizacaoWorkbookRowParser
{
    /// <summary>Column D's name as reported in row errors.</summary>
    public const string LampTypeColumn = "Tipo de Lâmpada";

    /// <summary>Column E's name as reported in row errors.</summary>
    public const string WattageColumn = "Potência (W)";

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

        if (!TryParseLampColumns(raw, rowNumber, errors, out var lampCode, out var wattageW))
        {
            return;
        }

        rows.Add(new FiscalizacaoWorkbookRow(codigo, operationGroup, quantity, lampCode, wattageW));
    }

    /// <summary>
    /// Reads the optional lamp columns D/E (XD01-301). Returns <c>true</c> with both outputs
    /// <c>null</c> when both cells are blank or missing (the pre-XD01-301 layout), <c>true</c>
    /// with both set when both are valid, and <c>false</c> — after recording one row error —
    /// otherwise.
    /// </summary>
    private static bool TryParseLampColumns(
        IReadOnlyList<string?> raw,
        int rowNumber,
        List<FiscalizacaoUploadRowError> errors,
        out Models.LampTypeCode? lampCode,
        out int? wattageW)
    {
        lampCode = null;
        wattageW = null;

        var rawLamp = Cell(raw, 3)?.Trim();
        var rawWattage = Cell(raw, 4)?.Trim();
        var hasLamp = !string.IsNullOrEmpty(rawLamp);
        var hasWattage = !string.IsNullOrEmpty(rawWattage);

        if (!hasLamp && !hasWattage)
        {
            return true;
        }

        if (!hasLamp)
        {
            errors.Add(new FiscalizacaoUploadRowError(rowNumber, LampTypeColumn, $"Required when {WattageColumn} is informed."));
            return false;
        }

        if (!hasWattage)
        {
            errors.Add(new FiscalizacaoUploadRowError(rowNumber, WattageColumn, $"Required when {LampTypeColumn} is informed."));
            return false;
        }

        if (!TryParseLampTypeCode(rawLamp!, out var parsedLamp))
        {
            errors.Add(new FiscalizacaoUploadRowError(
                rowNumber,
                LampTypeColumn,
                $"'{rawLamp}' is not a recognized lamp type (expected one of {string.Join(", ", Enum.GetNames<Models.LampTypeCode>())})."));
            return false;
        }

        if (!TryParsePositiveInt(rawWattage, out var parsedWattage))
        {
            errors.Add(new FiscalizacaoUploadRowError(rowNumber, WattageColumn, $"'{rawWattage}' is not a valid wattage."));
            return false;
        }

        lampCode = parsedLamp;
        wattageW = parsedWattage;
        return true;
    }

    /// <summary>Accepts only a <see cref="Models.LampTypeCode"/> member name (case-insensitive) —
    /// never its numeric value or a comma-separated list, both of which <c>Enum.TryParse</c>
    /// alone would let through.</summary>
    private static bool TryParseLampTypeCode(string raw, out Models.LampTypeCode code)
    {
        code = default;
        return raw.All(char.IsAsciiLetter) && Enum.TryParse(raw, ignoreCase: true, out code);
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
