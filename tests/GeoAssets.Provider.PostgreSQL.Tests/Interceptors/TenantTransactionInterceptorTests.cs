using FluentAssertions;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Interceptors;
using GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests.Interceptors;

/// <summary>
/// Proves <see cref="TenantTransactionInterceptor"/> closes the gap <see cref="TenantSessionInterceptor"/>
/// leaves open under PgBouncer <c>pool_mode = transaction</c>: a single physical connection
/// multiplexed across transactions belonging to different tenants. Simulates that multiplexing
/// directly — one real <see cref="NpgsqlConnection"/> kept open for the duration of a single test
/// and reused across several <see cref="GeoAssetsDbContext"/> instances (each standing in for a
/// different client session PgBouncer handed the same physical connection to), alternating which
/// tenant's <see cref="FakeTenantContext"/> is wired into the interceptor per transaction.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantTransactionInterceptorTests(PostgresContainerFixture fixture)
{
    private static readonly Guid OrgA = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OrgB = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static GeoAssetsDbContext CreateContextForTenant(NpgsqlConnection physicalConnection, Guid organizationId)
    {
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(physicalConnection, npgsql => npgsql.UseNetTopologySuite())
            .AddInterceptors(new TenantTransactionInterceptor(new FakeTenantContext(organizationId)))
            .Options;
        return new GeoAssetsDbContext(options);
    }

    private static async Task<string?> ReadCurrentOrganizationSettingAsync(
        NpgsqlConnection physicalConnection,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = physicalConnection.CreateCommand();
        command.CommandText = "SELECT current_setting('app.current_organization_id', true)";
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync();
        return result as string;
    }

    [Fact]
    public async Task TransactionStarted_AlternatingTenantsOverOnePhysicalConnection_NeverLeaksPriorTransactionsSetting()
    {
        await using var physicalConnection = new NpgsqlConnection(fixture.ConnectionString);
        await physicalConnection.OpenAsync();

        await using (var contextA = CreateContextForTenant(physicalConnection, OrgA))
        {
            await using var transactionA = await contextA.Database.BeginTransactionAsync();

            (await ReadCurrentOrganizationSettingAsync(physicalConnection, transactionA.GetDbTransaction() as NpgsqlTransaction))
                .Should().Be(OrgA.ToString(), because: "TransactionStartedAsync must stamp the setting for the tenant that owns this transaction");

            await transactionA.CommitAsync();
        }

        (await ReadCurrentOrganizationSettingAsync(physicalConnection))
            .Should().BeNullOrEmpty(because: "is_local=true scopes the setting to the transaction, so COMMIT must discard it rather than letting it linger on the physical connection for whoever PgBouncer hands the connection to next");

        await using (var contextB = CreateContextForTenant(physicalConnection, OrgB))
        {
            await using var transactionB = await contextB.Database.BeginTransactionAsync();

            (await ReadCurrentOrganizationSettingAsync(physicalConnection, transactionB.GetDbTransaction() as NpgsqlTransaction))
                .Should().Be(OrgB.ToString(), because: "starting OrgB's transaction on the same physical connection must not inherit OrgA's prior setting");

            await transactionB.CommitAsync();
        }

        await using (var contextA2 = CreateContextForTenant(physicalConnection, OrgA))
        {
            await using var transactionA2 = await contextA2.Database.BeginTransactionAsync();

            (await ReadCurrentOrganizationSettingAsync(physicalConnection, transactionA2.GetDbTransaction() as NpgsqlTransaction))
                .Should().Be(OrgA.ToString(), because: "alternating back to OrgA on the same physical connection must not inherit OrgB's prior setting either");

            await transactionA2.CommitAsync();
        }

        NpgsqlConnection.ClearAllPools();
    }

    [Fact]
    public async Task TransactionStarted_TransactionRolledBack_SettingDoesNotLeakToNextTransaction()
    {
        await using var physicalConnection = new NpgsqlConnection(fixture.ConnectionString);
        await physicalConnection.OpenAsync();

        await using (var contextA = CreateContextForTenant(physicalConnection, OrgA))
        {
            await using var transactionA = await contextA.Database.BeginTransactionAsync();

            (await ReadCurrentOrganizationSettingAsync(physicalConnection, transactionA.GetDbTransaction() as NpgsqlTransaction))
                .Should().Be(OrgA.ToString());

            await transactionA.RollbackAsync();
        }

        await using (var contextB = CreateContextForTenant(physicalConnection, OrgB))
        {
            await using var transactionB = await contextB.Database.BeginTransactionAsync();

            (await ReadCurrentOrganizationSettingAsync(physicalConnection, transactionB.GetDbTransaction() as NpgsqlTransaction))
                .Should().Be(OrgB.ToString(), because: "a rolled-back transaction must discard its is_local setting just like a committed one, so the next transaction on the same connection never inherits it");

            await transactionB.CommitAsync();
        }

        NpgsqlConnection.ClearAllPools();
    }
}
