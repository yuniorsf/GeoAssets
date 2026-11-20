using System.Globalization;
using Npgsql;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// One open connection to one of the two databases, with the session settings that make every value's text form exact
/// and stable — UTC, ISO dates, shortest-exact floats, hex bytea — and <c>row_security = off</c>, so a query that row-level
/// security would filter fails instead of silently returning fewer rows. Every value is read as text (the statements
/// cast each column with <c>::text</c>): two rows are equal exactly when their text forms are, and a value written back
/// from its text form is the same value. At most one transaction at a time; every command joins it.
/// </summary>
internal sealed class PostgresSession : IAsyncDisposable
{
    /// <summary>The <c>application_name</c> the tool connects with, unless the connection string sets one.</summary>
    public const string ApplicationName = "geoassets-neutral-operator-data-move";

    private const string SessionSettings =
        "SET TIME ZONE 'UTC'; SET DateStyle = 'ISO, YMD'; SET IntervalStyle = 'postgres'; " +
        "SET extra_float_digits = 3; SET bytea_output = 'hex'; SET row_security = off";

    private readonly NpgsqlConnection _connection;
    private NpgsqlTransaction? _transaction;

    private PostgresSession(string role, NpgsqlConnection connection, string connectionString, string description)
    {
        Role = role;
        _connection = connection;
        ConnectionString = connectionString;
        Description = description;
    }

    /// <summary>Which side this is, for messages: <c>kernel</c> or <c>neutral-operator</c>.</summary>
    public string Role { get; }

    /// <summary>Host, port and database — never the credentials.</summary>
    public string Description { get; }

    /// <summary>The connection string, for the EF migrations check only; never written anywhere.</summary>
    internal string ConnectionString { get; }

    public bool InTransaction => _transaction is not null;

    public static async Task<PostgresSession> OpenAsync(string role, string connectionString, CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrEmpty(builder.ApplicationName))
            builder.ApplicationName = ApplicationName;

        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            var settings = new NpgsqlCommand(SessionSettings, connection);
            await using (settings.ConfigureAwait(false))
                await settings.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new PostgresSession(role, connection, builder.ConnectionString, Describe(builder));
    }

    /// <summary><c>host:port/database</c> of <paramref name="builder"/>.</summary>
    internal static string Describe(NpgsqlConnectionStringBuilder builder) =>
        string.Create(CultureInfo.InvariantCulture, $"{builder.Host}:{builder.Port}/{builder.Database}");

    /// <summary>Runs a query whose every column is text and returns its rows (callers bound them with <c>LIMIT</c>).</summary>
    public async Task<List<string?[]>> QueryAsync(SqlStatement statement, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var command = Command(statement);
        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                var rows = new List<string?[]>();
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var row = new string?[reader.FieldCount];
                    for (var i = 0; i < row.Length; i++)
                        row[i] = await reader.IsDBNullAsync(i, ct).ConfigureAwait(false) ? null : reader.GetString(i);
                    rows.Add(row);
                }

                return rows;
            }
        }
    }

    /// <summary>The single text value of a one-row, one-column query.</summary>
    public async Task<string?> ScalarAsync(SqlStatement statement, CancellationToken ct)
    {
        var rows = await QueryAsync(statement, ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0][0];
    }

    /// <summary>A <c>count(*)::text</c> query's value.</summary>
    public async Task<long> CountAsync(SqlStatement statement, CancellationToken ct) =>
        long.Parse(await ScalarAsync(statement, ct).ConfigureAwait(false) ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <summary>Runs a statement and returns the rows it affected.</summary>
    public async Task<int> ExecuteAsync(SqlStatement statement, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var command = Command(statement);
        await using (command.ConfigureAwait(false))
            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task BeginAsync(CancellationToken ct)
    {
        if (_transaction is not null)
            throw new InvalidOperationException($"The {Role} session already has a transaction open.");

        _transaction = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        var transaction = _transaction ?? throw new InvalidOperationException($"The {Role} session has no transaction open.");
        _transaction = null;
        await using (transaction.ConfigureAwait(false))
            await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Rolls the open transaction back, if any. Not cancellable: it runs on the way out of a failure.</summary>
    public async Task RollbackAsync()
    {
        var transaction = _transaction;
        if (transaction is null) return;

        _transaction = null;
        await using (transaction.ConfigureAwait(false))
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await RollbackAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private NpgsqlCommand Command(SqlStatement statement)
    {
        // No timeout: a one-off move over a whole table may run long; Ctrl+C cancels it (and rolls it back).
        var command = new NpgsqlCommand(statement.Text, _connection, _transaction) { CommandTimeout = 0 };
        foreach (var parameter in statement.Parameters)
            command.Parameters.Add(parameter);
        return command;
    }
}
