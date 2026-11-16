using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

/// <summary>
/// XD01-332 — which organization a service order is under: fixed at creation, the creator's
/// own, and never changed by an update, in the domain (<see cref="ServiceOrderOrganization"/>)
/// and through <see cref="ValidatingServiceOrderRepository"/> over any store.
/// </summary>
public class ServiceOrderOrganizationTests
{
    private static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000332");
    private static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000332");

    private static ServiceOrder Order(Guid organizationId, string title = "t", string id = "ord-1") =>
        new() { Id = id, Title = title, Status = ServiceOrderStatus.Draft, OrganizationId = organizationId, CreatedBy = "creator" };

    // ── Creation ───────────────────────────────────────────────────────────────

    [Fact]
    public void XD01_332_ForCreation_OrderStatingTheCreatorsOrganization_IsStoredAsIs()
    {
        var order = Order(OrgA);

        ServiceOrderOrganization.ForCreation(order, OrgA).Should().BeSameAs(order);
    }

    [Fact]
    public void XD01_332_ForCreation_OrderStatingNoOrganization_IsStampedWithTheCreators()
    {
        var order = Order(Guid.Empty);

        var stamped = ServiceOrderOrganization.ForCreation(order, OrgA);

        stamped.OrganizationId.Should().Be(OrgA);
        stamped.Id.Should().Be(order.Id);
    }

    [Fact]
    public void XD01_332_ForCreation_CreatorWithNoOrganization_OrderStatingNone_StaysWithoutOne()
    {
        var order = Order(Guid.Empty);

        ServiceOrderOrganization.ForCreation(order, Guid.Empty).Should().BeSameAs(order);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void XD01_332_ForCreation_OrderStatingAnotherOrganization_IsRefused(bool creatorHasAnOrganization)
    {
        var act = () => ServiceOrderOrganization.ForCreation(Order(OrgB), creatorHasAnOrganization ? OrgA : Guid.Empty);

        act.Should().Throw<ServiceOrderOrganizationException>().Which.OrderId.Should().Be("ord-1");
    }

    // ── Update ─────────────────────────────────────────────────────────────────

    [Fact]
    public void XD01_332_ForUpdate_SameOrganization_IsStoredAsIs()
    {
        var incoming = Order(OrgA, "edited");

        ServiceOrderOrganization.ForUpdate(Order(OrgA), incoming).Should().BeSameAs(incoming);
    }

    [Fact]
    public void XD01_332_ForUpdate_IncomingStatingNoOrganization_KeepsTheStoredOne()
    {
        var kept = ServiceOrderOrganization.ForUpdate(Order(OrgA), Order(Guid.Empty, "edited"));

        kept.OrganizationId.Should().Be(OrgA);
        kept.Title.Should().Be("edited");
    }

    [Fact]
    public void XD01_332_ForUpdate_IncomingStatingAnotherOrganization_IsRefused()
    {
        var act = () => ServiceOrderOrganization.ForUpdate(Order(OrgA), Order(OrgB));

        act.Should().Throw<ServiceOrderOrganizationException>().Which.OrderId.Should().Be("ord-1");
    }

    [Fact]
    public void XD01_332_ForUpdate_OrderStoredWithNoOrganization_CannotBeClaimedByStatingOne()
    {
        var act = () => ServiceOrderOrganization.ForUpdate(Order(Guid.Empty), Order(OrgB));

        act.Should().Throw<ServiceOrderOrganizationException>();
    }

    [Fact]
    public void XD01_332_CopyWithOrganization_ChangesNothingButTheOrganization()
    {
        var feature = new GeoFeature { Id = "f-1" };
        var original = new ServiceOrder
        {
            Id = "ord-9",
            Title = "title",
            Description = "description",
            OrderTypeId = "inspection",
            RowVersion = [1, 2, 3],
            Status = ServiceOrderStatus.Pending,
            Priority = ServiceOrderPriority.High,
            CreatedBy = "creator",
            AssignedTo = "assignee",
            OrganizationId = Guid.Empty,
            CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
            ScheduledAt = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc),
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" },
            Features = [feature],
            SelectionSpec = new FeatureSelectionSpec { StrategyId = "manual", ExecutedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) },
            FeatureIds = ["f-1"],
            ParentOrderId = "parent",
            ChildOrderIds = ["child"],
            Dispatches = [new OrderDispatch("u", DispatchTargetType.User, "d", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc))],
            ActionLog = [new OrderActionLog(OrderActionType.Annotate, "u", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc))],
        };

        var copy = original.CopyWithOrganization(OrgA);

        copy.OrganizationId.Should().Be(OrgA);
        copy.Should().BeEquivalentTo(original, options => options.Excluding(o => o.OrganizationId));
    }

    // ── Through the repository decorator ───────────────────────────────────────

    [Fact]
    public async Task XD01_332_ValidatingRepository_UpdateAsync_StatingAnotherOrganization_IsRefused_AndTheStoreIsUntouched()
    {
        var inner = new FakeServiceOrderRepository();
        var repository = new ValidatingServiceOrderRepository(inner);
        await repository.AddAsync(Order(OrgA, "original"));

        var act = () => repository.UpdateAsync(Order(OrgB, "relabelled"));

        await act.Should().ThrowAsync<ServiceOrderOrganizationException>();
        var stored = await inner.GetByIdAsync("ord-1");
        stored!.OrganizationId.Should().Be(OrgA);
        stored.Title.Should().Be("original");
    }

    [Fact]
    public async Task XD01_332_ValidatingRepository_UpdateAsync_StatingNoOrganization_KeepsTheStoredOne_EvenOverAStoreThatReplacesTheOrder()
    {
        // FakeServiceOrderRepository replaces the stored instance with whatever it is handed —
        // the worst case for a snapshot that omits the organization.
        var inner = new FakeServiceOrderRepository();
        var repository = new ValidatingServiceOrderRepository(inner);
        await repository.AddAsync(Order(OrgA, "original"));

        await repository.UpdateAsync(Order(Guid.Empty, "edited"));

        var stored = await inner.GetByIdAsync("ord-1");
        stored!.OrganizationId.Should().Be(OrgA);
        stored.Title.Should().Be("edited");
    }

    [Fact]
    public async Task XD01_332_ValidatingRepository_UpdateAsync_SameOrganization_IsUnchangedBehaviour()
    {
        var inner = new FakeServiceOrderRepository();
        var repository = new ValidatingServiceOrderRepository(inner);
        await repository.AddAsync(Order(OrgA, "original"));
        var edited = Order(OrgA, "edited");

        await repository.UpdateAsync(edited);

        (await inner.GetByIdAsync("ord-1")).Should().BeSameAs(edited);
    }
}
