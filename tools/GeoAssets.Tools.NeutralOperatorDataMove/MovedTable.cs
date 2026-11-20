namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>Which rows of a table move.</summary>
internal enum MovedRowScope
{
    /// <summary><c>ServiceOrders</c> of a NeutralOperator order type, parents before children.</summary>
    OrderTree,

    /// <summary>
    /// A moved order's history rows (<c>OrderDispatches</c>, <c>OrderActionLogs</c>), compared and copied per order as a
    /// sequence: their integer identity is a database-local surrogate (no API returns it; reads order by the instant), and
    /// both databases number from 1, so the target assigns its own — in the source's order.
    /// </summary>
    OrderSequence,

    /// <summary>A moved order's <c>ServiceOrderTargets</c> rows held under the kernel vertical.</summary>
    OrderTargets,

    /// <summary>A moved order's undelivered <c>WorkflowOutboxMessages</c> of the module's own message types.</summary>
    PendingOutbox,

    /// <summary>Every row (the <c>neutral_operator</c> schema: the kernel's copy of the vertical's tables moves whole).</summary>
    WholeTable,
}

/// <summary>A table the move reads, compares, copies and removes — names from the EF model.</summary>
internal sealed class MovedTable
{
    public required string Schema { get; init; }

    public required string Name { get; init; }

    public required MovedRowScope Scope { get; init; }

    /// <summary>Every column the model maps (validated against both databases' catalogs: no more, no fewer).</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>The primary key's columns.</summary>
    public required IReadOnlyList<string> KeyColumns { get; init; }

    /// <summary>The columns two rows must agree on to be identical.</summary>
    public required IReadOnlyList<string> ComparedColumns { get; init; }

    /// <summary>The columns an insert writes (all but an <see cref="MovedRowScope.OrderSequence"/> table's identity).</summary>
    public required IReadOnlyList<string> CopiedColumns { get; init; }

    /// <summary>The unique indexes and alternate keys besides the primary key, on which a missing row may still collide.</summary>
    public required IReadOnlyList<UniqueIndex> UniqueIndexes { get; init; }

    public string QualifiedName => SqlStatement.Qualified(Schema, Name);

    public string DisplayName => Schema + "." + Name;

    /// <summary>The position of <paramref name="column"/> in <see cref="Columns"/>.</summary>
    public int IndexOf(string column)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (string.Equals(Columns[i], column, StringComparison.Ordinal)) return i;
        }

        throw new InvalidOperationException($"{DisplayName} has no column '{column}'.");
    }
}

/// <summary>A unique index (or alternate key) of a moved table: its columns and, for a partial one, its predicate as the model writes it.</summary>
internal sealed record UniqueIndex(IReadOnlyList<string> Columns, string? Filter);

/// <summary>A <see cref="MovedTable"/> with each column's base type (as <c>format_type(type, NULL)</c> names it), for casts.</summary>
internal sealed class ResolvedTable(MovedTable table, IReadOnlyDictionary<string, string> types)
{
    public MovedTable Table { get; } = table;

    /// <summary>The base type of <paramref name="column"/>, without its modifier — a cast to it never truncates.</summary>
    public string TypeOf(string column) => types[column];
}
