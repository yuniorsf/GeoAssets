using GeoAssets.IpBilling.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// Real SQLite database backing a single held-open connection — SQLite drops a
/// "Data Source=:memory:" database once the last connection to it closes, so this keeps one
/// alive for the fixture's lifetime while letting <see cref="NewContext"/> hand out additional
/// <see cref="IpBillingDbContext"/> instances bound to the same connection/schema/data.
/// Mirrors <c>GeoAssets.Workflow.EFCore.Tests.SqliteFixture</c>, minus that fixture's
/// RowVersion-trigger workaround — none of this ticket's entities carry a concurrency token.
/// </summary>
internal sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public IpBillingDbContext Context { get; }

    public SqliteFixture()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Context = NewContext();
        Context.Database.EnsureCreated();
    }

    public IpBillingDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<IpBillingDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new IpBillingDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
