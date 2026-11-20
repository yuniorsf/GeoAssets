using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>Plain SQL against a test database, as its connecting role (through a bare <see cref="DbContext"/>, never the tool's own code).</summary>
internal static class Sql
{
    public static async Task ExecAsync(string connectionString, string sql)
    {
        var db = Context(connectionString);
        await using (db.ConfigureAwait(false))
            await db.Database.ExecuteSqlAsync(Literal(sql)).ConfigureAwait(false);
    }

    /// <summary>The first column of every row of <paramref name="sql"/>, as text (null stays null).</summary>
    public static async Task<List<string?>> ColumnAsync(string connectionString, string sql)
    {
        var db = Context(connectionString);
        await using (db.ConfigureAwait(false))
            return await db.Database.SqlQuery<string?>(Literal(string.Concat("SELECT q.c::text AS \"Value\" FROM (", sql, ") AS q(c)"))).ToListAsync().ConfigureAwait(false);
    }

    public static async Task<string> ScalarAsync(string connectionString, string sql) =>
        Assert.Single(await ColumnAsync(connectionString, sql).ConfigureAwait(false)) ?? string.Empty;

    public static async Task<long> CountAsync(string connectionString, string table, string? where = null) =>
        long.Parse(
            await ScalarAsync(connectionString, $"SELECT count(*) FROM {table}{(where is null ? string.Empty : " WHERE " + where)}").ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The text as a format string with no holes: its braces (JSON) are literal.</summary>
    private static FormattableString Literal(string sql) =>
        System.Runtime.CompilerServices.FormattableStringFactory.Create(sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal));

    private static DbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<DbContext>().UseNpgsql(connectionString).Options);
}
