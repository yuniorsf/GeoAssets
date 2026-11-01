using Npgsql;

namespace GeoAssets.Provider.PostgreSQL;

/// <summary>
/// Connection-string helpers for the shared PostgreSQL tier's "noisy neighbor" mitigations
/// (XD01-214) — complements the gateway's per-OrganizationId rate limiting with a server-side
/// backstop: even a request the gateway let through can't hold a connection/lock open
/// indefinitely and starve other tenants' queries on the same shared database.
/// </summary>
public static class PostgresConnectionStringHelper
{
    /// <summary>
    /// Appends Postgres' own <c>statement_timeout</c> session parameter (not Npgsql's
    /// client-side command timeout, a different setting) to <paramref name="connectionString"/>
    /// via libpq's <c>-c</c> startup option, so every statement on a connection opened with the
    /// result aborts after <paramref name="statementTimeout"/> instead of running indefinitely.
    /// Opt-in — callers decide which hosts/tiers need this (a dedicated silo's connection string
    /// may reasonably skip it).
    /// </summary>
    public static string WithStatementTimeout(string connectionString, TimeSpan statementTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (statementTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(statementTimeout), "Must be positive.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var statementTimeoutMilliseconds = (long)statementTimeout.TotalMilliseconds;
        var statementTimeoutOption = $"-c statement_timeout={statementTimeoutMilliseconds}";

        builder.Options = string.IsNullOrWhiteSpace(builder.Options)
            ? statementTimeoutOption
            : $"{builder.Options} {statementTimeoutOption}";

        return builder.ConnectionString;
    }
}
