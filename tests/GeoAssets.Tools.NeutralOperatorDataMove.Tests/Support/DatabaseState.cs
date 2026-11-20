using System.Globalization;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>A row count and a content hash per table: two captures are equal exactly when no row was added, removed or changed.</summary>
internal sealed class DatabaseState : IEquatable<DatabaseState>
{
    /// <summary>The tables the tool reads or writes, in the tool's own spelling.</summary>
    public static readonly string[] Tables =
    [
        "public.\"ServiceOrders\"",
        "public.\"OrderDispatches\"",
        "public.\"OrderActionLogs\"",
        "public.\"ServiceOrderTargets\"",
        "public.\"WorkflowOutboxMessages\"",
        "neutral_operator.field_pole_occupancies",
        "neutral_operator.field_pole_occupancy_entries",
        "neutral_operator.occupancy_apply_sagas",
        "neutral_operator.occupancy_apply_saga_poles",
        "neutral_operator.\"ServiceOrderOccupancyChangeSets\"",
    ];

    private DatabaseState(SortedDictionary<string, string> tables) => Digests = tables;

    /// <summary><c>count:hash</c> per table.</summary>
    public IReadOnlyDictionary<string, string> Digests { get; }

    public static async Task<DatabaseState> CaptureAsync(string connectionString)
    {
        var tables = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in Tables)
        {
            var digest = await Sql.ScalarAsync(
                connectionString,
                $"SELECT count(*)::text || ':' || md5(coalesce(string_agg(to_jsonb(t)::text, E'\\n' ORDER BY to_jsonb(t)::text), '')) FROM {table} t").ConfigureAwait(false);
            tables[table] = digest;
        }

        return new DatabaseState(tables);
    }

    public long RowsOf(string table) =>
        long.Parse(Digests[table].Split(':')[0], CultureInfo.InvariantCulture);

    /// <summary>The tables whose count or content differ from <paramref name="other"/> (<c>table: this vs other</c>).</summary>
    public IReadOnlyList<string> Differences(DatabaseState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return [.. Digests.Where(d => !other.Digests.TryGetValue(d.Key, out var value) || !string.Equals(value, d.Value, StringComparison.Ordinal))
            .Select(d => $"{d.Key}: {d.Value} vs {other.Digests.GetValueOrDefault(d.Key)}")];
    }

    public bool Equals(DatabaseState? other) =>
        other is not null && Digests.Count == other.Digests.Count && Digests.All(d => other.Digests.TryGetValue(d.Key, out var value) && string.Equals(value, d.Value, StringComparison.Ordinal));

    public override bool Equals(object? obj) => Equals(obj as DatabaseState);

    public override int GetHashCode() => Digests.Count;

    public override string ToString() => string.Join(Environment.NewLine, Digests.Select(d => $"{d.Key} = {d.Value}"));
}
