using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// Inserts rows of a mapped entity with plain <c>INSERT … VALUES (…), (…)</c> statements, past
/// the change tracker, so the write neither tracks anything nor flushes what else the shared
/// context happens to be tracking. Table and column names come from the EF model and every
/// value travels as a parameter typed by that column's own mapping, so a statement cannot drift
/// from the schema. Optionally <c>ON CONFLICT (…) DO NOTHING</c>, which PostgreSQL and SQLite —
/// the two engines <see cref="NeutralOperatorDbContext"/> runs on — both understand.
///
/// The same technique as the kernel target repository's insert, generalized for
/// the occupancy tables (XD01-314).
/// </summary>
internal static class RawRowInsert
{
    /// <summary>How many rows go into one statement.</summary>
    internal const int RowsPerStatement = 100;

    /// <summary>
    /// Inserts <paramref name="records"/> and returns how many rows were really written. It
    /// opens no transaction: more than one statement is only all-or-nothing inside the caller's.
    /// </summary>
    /// <param name="db">The context whose model and connection are used.</param>
    /// <param name="records">The rows.</param>
    /// <param name="columns">Every mapped property of <typeparamref name="TRecord"/>, with how to read its value.</param>
    /// <param name="skipOnConflict">
    /// Properties of a unique index: a row that collides on it is skipped instead of failing.
    /// Null inserts unconditionally (a collision is the provider's exception). An empty list
    /// skips a row that collides on <b>any</b> unique index — the form a partial unique index
    /// needs, which PostgreSQL cannot infer from a column list alone.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<int> ExecuteAsync<TRecord>(
        DbContext db,
        IReadOnlyCollection<TRecord> records,
        IReadOnlyList<(string Property, Func<TRecord, object?> Value)> columns,
        IReadOnlyList<string>? skipOnConflict,
        CancellationToken ct)
        where TRecord : class
    {
        if (records.Count == 0) return 0;

        var entityType = db.Model.FindEntityType(typeof(TRecord))
            ?? throw new InvalidOperationException($"{typeof(TRecord).Name} is not part of the model.");
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var sql = db.GetService<ISqlGenerationHelper>();
        var properties = columns.Select(column => entityType.GetProperty(column.Property)).ToList();

        string Column(IProperty property) => sql.DelimitIdentifier(property.GetColumnName(table)!);

        var head = $"INSERT INTO {sql.DelimitIdentifier(table.Name, table.Schema)} ({string.Join(", ", properties.Select(Column))}) VALUES ";
        var tail = skipOnConflict switch
        {
            null => string.Empty,
            { Count: 0 } => " ON CONFLICT DO NOTHING",
            _ => $" ON CONFLICT ({string.Join(", ", skipOnConflict.Select(name => Column(entityType.GetProperty(name))))}) DO NOTHING",
        };

        // Only a factory for provider-typed parameters; it is never executed.
        var parameterFactory = db.Database.GetDbConnection().CreateCommand();
        await using (parameterFactory.ConfigureAwait(false))
        {
            var inserted = 0;
            foreach (var batch in records.Chunk(RowsPerStatement))
            {
                var statement = new StringBuilder(head);
                var parameters = new List<object>(batch.Length * properties.Count);
                for (var row = 0; row < batch.Length; row++)
                {
                    statement.Append(row == 0 ? "(" : ", (");
                    for (var column = 0; column < properties.Count; column++)
                    {
                        var name = sql.GenerateParameterName(string.Create(CultureInfo.InvariantCulture, $"r{row}_{column}"));
                        var value = columns[column].Value(batch[row]);
                        parameters.Add(properties[column].GetRelationalTypeMapping().CreateParameter(parameterFactory, name, value, properties[column].IsNullable));
                        statement.Append(column == 0 ? string.Empty : ", ").Append(name);
                    }

                    statement.Append(')');
                }

                statement.Append(tail);
                inserted += await db.Database.ExecuteSqlRawAsync(statement.ToString(), parameters, ct).ConfigureAwait(false);
            }

            return inserted;
        }
    }
}
