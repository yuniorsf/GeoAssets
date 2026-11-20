namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>Comparisons and keys over rows read as text (<see cref="PostgresSession"/>).</summary>
internal static class RowValues
{
    private const char Separator = '\u001f';

    /// <summary>Whether <paramref name="left"/> and <paramref name="right"/> agree on every column at <paramref name="indexes"/>.</summary>
    public static bool Same(string?[] left, string?[] right, IReadOnlyList<int> indexes)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(indexes);

        foreach (var i in indexes)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) return false;
        }

        return true;
    }

    /// <summary>The primary key of <paramref name="row"/> as one dictionary key.</summary>
    public static string Key(string?[] row, IReadOnlyList<int> keyIndexes) =>
        Key(keyIndexes.Select(i => row[i]));

    /// <summary>Key values, in key-column order, as one dictionary key.</summary>
    public static string Key(IEnumerable<string?> parts) => string.Join(Separator, parts);

    /// <summary>The primary key of <paramref name="row"/> as a report shows it.</summary>
    public static string Display(string?[] row, IReadOnlyList<int> keyIndexes) =>
        string.Join("/", keyIndexes.Select(i => row[i] ?? "NULL"));

    /// <summary>The columns that differ between two rows (names only: values may be personal data).</summary>
    public static string DifferingColumns(MovedTable table, string?[] left, string?[] right, IReadOnlyList<int> indexes)
    {
        ArgumentNullException.ThrowIfNull(table);

        return string.Join(", ", indexes.Where(i => !string.Equals(left[i], right[i], StringComparison.Ordinal)).Select(i => table.Columns[i]));
    }
}
