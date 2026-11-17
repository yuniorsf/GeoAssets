using System.Globalization;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// Reads a comma-separated export of the legacy <c>SharedUses</c> table. The first line is the
/// header; columns are found by name (case-insensitive), so their order and any extra column
/// (<c>Level</c>, <c>LevDate</c>, …) do not matter. Blank lines are ignored. Anything malformed
/// fails the whole read with the line number: a damaged export is re-exported, not half imported.
/// </summary>
internal static class LegacySharedUseCsvParser
{
    private static readonly string[] RequiredColumns = ["Id", "PoleId", "OccupantId", "WireTypeId", "Count"];

    // Columns only the per-order shadow copy (SharedUsesSO) has; only the live table is imported.
    private static readonly string[] ShadowTableColumns = ["CopyId", "SoId", "TaskId"];

    /// <exception cref="InvalidDataException">The header or a line is malformed, or the file is the shadow table.</exception>
    public static async Task<IReadOnlyList<LegacySharedUseRow>> ParseAsync(TextReader reader, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var header = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(header))
            throw new InvalidDataException("The SharedUses file is empty: its first line must be the header.");

        var columns = ReadHeader(header);
        var rows = new List<LegacySharedUseRow>();
        var lineNumber = 1;
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = line.Split(',');
            if (fields.Length != columns.Count)
                throw new InvalidDataException(
                    $"Line {lineNumber} has {fields.Length} fields; the header has {columns.Count}.");

            rows.Add(new LegacySharedUseRow(
                Id: ReadInt(fields, columns, "Id", lineNumber),
                PoleId: ReadInt(fields, columns, "PoleId", lineNumber),
                OccupantId: ReadInt(fields, columns, "OccupantId", lineNumber),
                WireTypeId: ReadInt(fields, columns, "WireTypeId", lineNumber),
                Count: ReadInt(fields, columns, "Count", lineNumber),
                LineNumber: lineNumber));
        }

        return rows;
    }

    private static Dictionary<string, int> ReadHeader(string header)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var names = header.Split(',');
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i].Trim();
            if (!columns.TryAdd(name, i))
                throw new InvalidDataException($"The header names column '{name}' more than once.");
        }

        if (ShadowTableColumns.FirstOrDefault(columns.ContainsKey) is { } shadowColumn)
            throw new InvalidDataException(
                $"The header has column '{shadowColumn}': this is the per-order shadow table SharedUsesSO. Export the live SharedUses table.");

        if (RequiredColumns.FirstOrDefault(column => !columns.ContainsKey(column)) is { } missing)
            throw new InvalidDataException($"The header has no '{missing}' column.");

        return columns;
    }

    private static int ReadInt(string[] fields, Dictionary<string, int> columns, string column, int lineNumber)
    {
        var text = fields[columns[column]].Trim();
        return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"Line {lineNumber}: '{column}' is '{text}', not a whole number.");
    }
}
