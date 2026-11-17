using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// <see cref="EFServiceOrderRepository.GetDispatchedToAsync"/> as an organization's inbox
/// (XD01-316, finding B1): an organization lists the orders of other organizations that were
/// dispatched to it — and the target id a caller supplies never widens what it sees.
/// </summary>
public sealed class EFServiceOrderRepositoryDispatchInboxTests
{
    private static readonly Guid Owner = new("a0a0a0a0-0000-4000-8000-000000000316");
    private static readonly Guid Occupant = new("b0b0b0b0-0000-4000-8000-000000000316");
    private static readonly Guid OtherOccupant = new("c0c0c0c0-0000-4000-8000-000000000316");
    private static readonly DateTime At = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    private static ServiceOrder Order(string id, Guid organizationId, string? parentOrderId = null) => new()
    {
        Id = id,
        Title = id,
        OrderTypeId = "inspection",
        CreatedBy = "tester",
        OrganizationId = organizationId,
        ParentOrderId = parentOrderId,
        CreatedAt = At,
    };

    private static OrderDispatch To(Guid organizationId, bool upperCase = false) => new(
        upperCase ? organizationId.ToString("D").ToUpperInvariant() : organizationId.ToString("D"),
        DispatchTargetType.Organization, "dispatcher", At);

    /// <summary>
    /// The owner's parent order with one child per occupant, each dispatched to its occupant; an
    /// order the owner dispatched to a user of its own; and an order of the occupant itself.
    /// </summary>
    private static async Task SeedAsync(SqliteFixture fixture, bool upperCase = false)
    {
        await using var seed = fixture.NewContext(new FakeTenantContext(Owner));
        var repo = new EFServiceOrderRepository(seed);
        await repo.AddAsync(Order("parent", Owner));
        var forOccupant = Order("child-occupant", Owner, "parent");
        forOccupant.Dispatches.Add(To(Occupant, upperCase));
        await repo.AddAsync(forOccupant);
        var forOther = Order("child-other", Owner, "parent");
        forOther.Dispatches.Add(To(OtherOccupant));
        await repo.AddAsync(forOther);
        var internalOrder = Order("owner-internal", Owner);
        internalOrder.Dispatches.Add(new OrderDispatch("crew-1", DispatchTargetType.Group, "dispatcher", At));
        await repo.AddAsync(internalOrder);

        await using var occupantSeed = fixture.NewContext(new FakeTenantContext(Occupant));
        var own = Order("occupant-own", Occupant);
        own.Dispatches.Add(To(Occupant));
        await new EFServiceOrderRepository(occupantSeed).AddAsync(own);
    }

    private static async Task<List<string>> InboxAsync(
        SqliteFixture fixture, Guid caller, string targetId, DispatchTargetType targetType = DispatchTargetType.Organization)
    {
        await using var context = fixture.NewContext(new FakeTenantContext(caller));
        var orders = await new EFServiceOrderRepository(context).GetDispatchedToAsync(targetId, targetType);
        return [.. orders.Select(o => o.Id).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task AnOrganizationAskingForItsOwnInbox_GetsTheOrdersOfOtherOrganizationsDispatchedToIt_AndItsOwn()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);

        var inbox = await InboxAsync(fixture, Occupant, Occupant.ToString());

        inbox.Should().Equal("child-occupant", "occupant-own");
    }

    [Fact]
    public async Task TheInbox_CarriesEachOrderWithItsDispatches_SoTheOrganizationGateCanRecognizeTheRecipient()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);
        await using var context = fixture.NewContext(new FakeTenantContext(Occupant));

        var orders = await new EFServiceOrderRepository(context).GetDispatchedToAsync(Occupant.ToString(), DispatchTargetType.Organization);

        var received = orders.Single(o => string.Equals(o.Id, "child-occupant", StringComparison.Ordinal));
        received.OrganizationId.Should().Be(Owner, "the order stays the pole owner's");
        received.ParentOrderId.Should().Be("parent");
        received.Dispatches.Should().ContainSingle().Which.TargetId.Should().Be(Occupant.ToString("D"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TheInbox_MatchesAnOrganizationIdInEitherLetterCase(bool storedUpperCase, bool askedUpperCase)
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture, storedUpperCase);
        var asked = askedUpperCase ? Occupant.ToString("D").ToUpperInvariant() : Occupant.ToString("D");

        var inbox = await InboxAsync(fixture, Occupant, asked);

        inbox.Should().Contain("child-occupant");
    }

    [Fact]
    public async Task AskingForAnotherOrganizationsInbox_NeverReachesPastTheCallersOwnOrders()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);

        var asOccupant = await InboxAsync(fixture, Occupant, OtherOccupant.ToString());
        var asStranger = await InboxAsync(fixture, Guid.NewGuid(), Occupant.ToString());

        asOccupant.Should().BeEmpty("the other occupant's order is not this caller's to list, whatever id it asks for");
        asStranger.Should().BeEmpty();
    }

    [Fact]
    public async Task TheOwner_StillSeesItsOwnOrdersByWhoeverTheyWereDispatchedTo()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);

        (await InboxAsync(fixture, Owner, Occupant.ToString())).Should().Equal("child-occupant");
        (await InboxAsync(fixture, Owner, OtherOccupant.ToString())).Should().Equal("child-other");
        (await InboxAsync(fixture, Owner, "crew-1", DispatchTargetType.Group)).Should().Equal("owner-internal");
        (await InboxAsync(fixture, Owner, Owner.ToString())).Should().BeEmpty("nothing was dispatched to the owner itself");
    }

    [Theory]
    [InlineData(DispatchTargetType.User)]
    [InlineData(DispatchTargetType.Group)]
    public async Task AUserOrGroupTarget_NeverReachesPastTheTenantFilter_EvenWhenItSpellsTheCallersOrganizationId(DispatchTargetType targetType)
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);
        await using (var seed = fixture.NewContext(new FakeTenantContext(Owner)))
        {
            var sneaky = Order("owner-to-user", Owner);
            sneaky.Dispatches.Add(new OrderDispatch(Occupant.ToString("D"), targetType, "dispatcher", At));
            await new EFServiceOrderRepository(seed).AddAsync(sneaky);
        }

        var inbox = await InboxAsync(fixture, Occupant, Occupant.ToString(), targetType);

        inbox.Should().BeEmpty("only a dispatch to the organization opens another organization's order");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ATargetThatIsNotARealOrganizationId_IsAnOrdinaryFilteredLookup(string targetId)
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);

        var inbox = await InboxAsync(fixture, Guid.Empty, targetId);

        inbox.Should().BeEmpty();
    }

    [Fact]
    public async Task TheOtherListQueries_AreUnchanged_ARecipientDoesNotSeeTheOrderThroughThem()
    {
        using var fixture = new SqliteFixture();
        await SeedAsync(fixture);
        await using var context = fixture.NewContext(new FakeTenantContext(Occupant));
        var repo = new EFServiceOrderRepository(context);

        (await repo.GetAllAsync()).Select(o => o.Id).Should().Equal("occupant-own");
        (await repo.GetChildrenAsync("parent")).Should().BeEmpty();
        (await repo.GetByStatusAsync(ServiceOrderStatus.Draft)).Select(o => o.Id).Should().Equal("occupant-own");
        (await repo.GetByIdAsync("child-occupant")).Should().NotBeNull("by id an order is fetched unfiltered, and the organization gate decides");
    }
}
