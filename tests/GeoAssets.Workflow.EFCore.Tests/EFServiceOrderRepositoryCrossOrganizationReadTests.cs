using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// <see cref="EFServiceOrderRepository.GetOfOtherOrganizationsAsync"/> (XD01-328): the explicit, grant-aware read path past
/// the tenant filter reaches exactly the organizations asked for — never the caller's own, never <see cref="Guid.Empty"/> —
/// and leaves the filter in place for every other read of the same context. Its row-level-security behaviour is covered on
/// PostgreSQL by <see cref="ServiceOrdersRowLevelSecurityPolicyTests"/>.
/// </summary>
public sealed class EFServiceOrderRepositoryCrossOrganizationReadTests
{
    private static readonly Guid Bimbo = new("b1b1b1b1-0000-4000-8000-000000000328");
    private static readonly Guid Coelba = new("c0c0c0c0-0000-4000-8000-000000000328");
    private static readonly Guid Third = new("d0d0d0d0-0000-4000-8000-000000000328");
    private static readonly DateTime At = new(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);

    private static ServiceOrder Order(string id, Guid organizationId) => new()
    {
        Id = id,
        Title = id,
        OrderTypeId = "cut",
        CreatedBy = "tester",
        OrganizationId = organizationId,
        CreatedAt = At,
    };

    private static async Task SeedAsync(SqliteFixture fixture)
    {
        foreach (var (id, organization) in new[] { ("bimbo-1", Bimbo), ("coelba-1", Coelba), ("coelba-2", Coelba), ("third-1", Third), ("unowned", Guid.Empty) })
        {
            await using var seed = fixture.NewContext(new FakeTenantContext(organization));
            await new EFServiceOrderRepository(seed).AddAsync(Order(id, organization));
        }
    }

    private static async Task<List<string>> ReadAsync(SqliteFixture fixture, Guid caller, params Guid[] organizations)
    {
        await using var context = fixture.NewContext(new FakeTenantContext(caller));
        var orders = await new EFServiceOrderRepository(context).GetOfOtherOrganizationsAsync(organizations);
        return [.. orders.Select(o => o.Id).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task ReachesTheOrganizationsAskedFor_AndNoOther()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);

        (await ReadAsync(fixture, Bimbo, Coelba)).Should().Equal("coelba-1", "coelba-2");
        (await ReadAsync(fixture, Bimbo, Coelba, Third)).Should().Equal("coelba-1", "coelba-2", "third-1");
    }

    [Fact]
    public async Task NeverReturnsTheCallersOwnOrdersNorUnownedOnes_ThoseStayWithTheFilteredReads()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);

        (await ReadAsync(fixture, Bimbo, Bimbo, Guid.Empty)).Should().BeEmpty();
        (await ReadAsync(fixture, Bimbo)).Should().BeEmpty();
    }

    [Fact]
    public async Task LeavesTheTenantFilterOnForTheContextsOtherReads()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);
        await using var context = fixture.NewContext(new FakeTenantContext(Bimbo));
        var repo = new EFServiceOrderRepository(context);

        await repo.GetOfOtherOrganizationsAsync([Coelba]);
        var all = await repo.GetAllAsync();

        all.Select(o => o.Id).Should().Equal("bimbo-1");
    }

    [Fact]
    public async Task EachOrderCarriesItsOwningOrganization()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);
        await using var context = fixture.NewContext(new FakeTenantContext(Bimbo));

        var orders = await new EFServiceOrderRepository(context).GetOfOtherOrganizationsAsync([Coelba]);

        orders.Should().OnlyContain(o => o.OrganizationId == Coelba);
    }
}
