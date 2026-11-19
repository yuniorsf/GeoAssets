using GeoAssets.Core.Interfaces;
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
internal sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ITenantContext? _tenantContext;

    public ServiceOrderDbContext Context { get; }

    /// <param name="tenantContext">
    /// The organization every context of this fixture is scoped to; omitted, they resolve to
    /// <see cref="Guid.Empty"/> like a host with no tenant registered.
    /// </param>
    public SqliteFixture(ITenantContext? tenantContext = null)
    {
        _tenantContext = tenantContext;
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Context = NewContext();
        Context.Database.EnsureCreated();

        // The NeutralOperator vertical's tables (XD01-380) live in its own context, over this same connection. EnsureCreated
        // would see the kernel's tables and create nothing, so its create script is run instead (SQLite ignores the schema).
        // The change sets' foreign key to the kernel's targets is DB-only (the vertical's model does not know the kernel's
        // target entity — on PostgreSQL its baseline migration creates it), so it is added to the script here.
        using (var neutralOperator = NeutralOperatorTestContext.Over(Context))
        {
            var script = neutralOperator.Database.GenerateCreateScript();
            var tableStart = script.IndexOf("CREATE TABLE \"ServiceOrderOccupancyChangeSets\"", StringComparison.Ordinal);
            var tableEnd = tableStart < 0 ? -1 : script.IndexOf(");", tableStart, StringComparison.Ordinal);
            if (tableEnd < 0)
                throw new InvalidOperationException("The NeutralOperator create script no longer declares the change set table as expected.");

            while (char.IsWhiteSpace(script[tableEnd - 1]))
                tableEnd--;

            neutralOperator.Database.ExecuteSqlRaw(script.Insert(
                tableEnd,
                ",\n    CONSTRAINT \"FK_ServiceOrderOccupancyChangeSets_ServiceOrderTargets_TargetId\" FOREIGN KEY (\"TargetId\") REFERENCES \"ServiceOrderTargets\" (\"Id\") ON DELETE CASCADE"));
        }

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
    public ServiceOrderDbContext NewContext() => NewContext(_tenantContext);

    /// <summary>Same as <see cref="NewContext()"/>, but scoped to <paramref name="tenantContext"/> instead of the fixture's own tenant.</summary>
    public ServiceOrderDbContext NewContext(ITenantContext? tenantContext)
    {
        var options = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new SqliteTestDbContext(options, tenantContext);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
