using GeoAssets.Workflow.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Real SQLite database backing a single held-open connection — SQLite drops a
/// "Data Source=:memory:" database once the last connection to it closes, so this
/// keeps one alive for the fixture's lifetime while letting <see cref="NewContext"/>
/// hand out additional <see cref="ServiceOrderDbContext"/> instances bound to the
/// same connection/schema/data, e.g. to simulate two concurrent writers racing on
/// the same row (see <c>EFServiceOrderRepositoryTests</c>'s concurrency-conflict test).
///
/// Chosen over the <c>Microsoft.EntityFrameworkCore.InMemory</c> provider because that
/// provider does not enforce optimistic concurrency tokens at all — it would silently
/// let a stale write through instead of throwing, defeating the point of testing
/// <c>EFServiceOrderRepository</c>'s <c>RowVersion</c>-based conflict detection.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public ServiceOrderDbContext Context { get; }

    public SqliteFixture()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Context = NewContext();
        Context.Database.EnsureCreated();

        // SQLite has no native auto-updating rowversion column — this trigger is what
        // makes ServiceOrders.RowVersion actually change on every UPDATE, so it behaves
        // like the SQL Server target ServiceOrderRecordConfiguration.IsRowVersion() is
        // written for (the initial-INSERT value comes from SqliteTestDbContext's
        // SQLite-only HasDefaultValueSql, since SQLite triggers can't populate a column
        // before its own INSERT completes).
        Context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER ServiceOrders_RowVersion_Update AFTER UPDATE ON ServiceOrders
            BEGIN
                UPDATE ServiceOrders SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
            END;
            """);
    }

    /// <summary>
    /// A second <see cref="ServiceOrderDbContext"/> bound to the same connection/schema —
    /// its own change tracker is independent of <see cref="Context"/>'s, so entities
    /// loaded through it hold their own "original values" snapshot for concurrency checks.
    /// </summary>
    public ServiceOrderDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new SqliteTestDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
