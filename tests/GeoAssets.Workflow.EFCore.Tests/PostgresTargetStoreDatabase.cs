using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// One real PostgreSQL container for the service-order-target suites (XD01-306), started lazily
/// and shared through a static — the same pattern, and for the same CA1515 reason, as
/// <see cref="ServiceOrdersRowLevelSecurityPolicyTests"/>. Each test class asks for its own
/// database, so classes xUnit runs in parallel never see each other's rows; the container is
/// torn down with the test process.
///
/// The kernel schema comes from <c>EnsureCreatedAsync</c> (this project has no reference to the hosts
/// that carry the kernel migrations), i.e. straight from the EF model — including the PostgreSQL-only
/// <c>"C"</c> collation on the sortable target columns, which is part of the model. The
/// migrations themselves, and the row-level-security policy, are exercised by
/// GeoAssets.Security.Tests and GeoAssets.FieldService.Server.Tests.
/// </summary>
internal static class PostgresTargetStoreDatabase
{
    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static readonly Dictionary<string, string> ConnectionStrings = new(StringComparer.Ordinal);
    private static PostgreSqlContainer? _container;

    /// <summary>Returns the connection string of <paramref name="databaseName"/>, creating container and schema on first use.</summary>
    public static async Task<string> EnsureCreatedAsync(string databaseName)
    {
        await StartLock.WaitAsync();
        try
        {
            if (ConnectionStrings.TryGetValue(databaseName, out var existing)) return existing;

            if (_container is null)
            {
                var container = new PostgreSqlBuilder("postgres:16")
                    .WithDatabase("geoassets_target_store_test")
                    .WithUsername("geoassets")
                    .WithPassword("geoassets")
                    .Build();
                await container.StartAsync();
                _container = container;
            }

            var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
            {
                Database = databaseName,
            }.ConnectionString;

            await using (var db = NewContext(connectionString))
            {
                await db.Database.EnsureCreatedAsync();

                // The NeutralOperator vertical's tables (XD01-380) come from its own migration chain, on the kernel schema.
                await using var neutralOperator = NeutralOperatorTestContext.Over(db);
                await neutralOperator.Database.MigrateAsync();
            }

            ConnectionStrings[databaseName] = connectionString;
            return connectionString;
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>A context on its own pooled connection, so two of them are two real database sessions.</summary>
    public static ServiceOrderDbContext NewContext(string connectionString, ITenantContext? tenantContext = null) =>
        new PostgresTestDbContext(
            new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql(connectionString).Options,
            tenantContext);

    /// <summary>Empties every order (and, by cascade, every target) of the database.</summary>
    public static async Task ClearAsync(string connectionString)
    {
        await using var db = NewContext(connectionString);
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE "ServiceOrders" CASCADE;""");
    }

    /// <summary>
    /// In the migrated schema a trigger fills <c>"ServiceOrders"."RowVersion"</c>; a schema built
    /// by <c>EnsureCreated</c> has no trigger, so inserting an order through
    /// <see cref="EFServiceOrderRepository"/> needs a column default instead — the PostgreSQL
    /// counterpart of <see cref="SqliteTestDbContext"/>. Nothing about targets is altered.
    /// </summary>
    private sealed class PostgresTestDbContext(DbContextOptions<ServiceOrderDbContext> options, ITenantContext? tenantContext)
        : ServiceOrderDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ServiceOrderRecord>()
                .Property(o => o.RowVersion)
                .HasDefaultValueSql("'\\x01'::bytea");
        }
    }
}
