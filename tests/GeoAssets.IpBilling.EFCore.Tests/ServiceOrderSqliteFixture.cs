using GeoAssets.Workflow.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// Real SQLite database backing a single held-open connection, letting <see cref="NewContext"/>
/// hand out additional <see cref="ServiceOrderDbContext"/> instances bound to the same
/// connection/schema/data — used to simulate two concurrent writers racing on the same
/// <c>ServiceOrder</c> row via its <c>RowVersion</c> optimistic-concurrency token (XD01-180's
/// AC). Mirrors <c>GeoAssets.Workflow.EFCore.Tests.SqliteFixture</c> (not reusable across
/// assemblies, hence the local copy).
/// </summary>
internal sealed class ServiceOrderSqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public ServiceOrderDbContext Context { get; }

    public ServiceOrderSqliteFixture()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Context = NewContext();
        Context.Database.EnsureCreated();

        Context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER ServiceOrders_RowVersion_Update AFTER UPDATE ON ServiceOrders
            BEGIN
                UPDATE ServiceOrders SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
            END;
            """);
    }

    public ServiceOrderDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ServiceOrderSqliteTestDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
