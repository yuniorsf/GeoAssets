using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>Reads what the host's database really holds, as its owner (row-level security does not apply to it), as text.</summary>
internal static class DatabaseProbe
{
    /// <summary>Runs <paramref name="sql"/> (a single column aliased <c>"Value"</c>) on the host's own context.</summary>
    public static async Task<List<string>> ValuesAsync(string connectionString, string sql)
    {
        await using var db = NeutralOperatorDatabase.ServiceOrderContext(connectionString);
        return await db.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }

    public static async Task<string> ValueAsync(string connectionString, string sql) =>
        Assert.Single(await ValuesAsync(connectionString, sql));
}
