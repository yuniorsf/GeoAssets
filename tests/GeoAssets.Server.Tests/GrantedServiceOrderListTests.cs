using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>XD01-408 — <see cref="GrantedServiceOrderList"/>: which organizations the read path is asked for, and how the list is composed.</summary>
public sealed class GrantedServiceOrderListTests
{
    private static readonly Guid Own = new("a0a0a0a0-0000-4000-8000-000000000408");
    private static readonly Guid Granting = new("b0b0b0b0-0000-4000-8000-000000000408");
    private static readonly Guid Other = new("c0c0c0c0-0000-4000-8000-000000000408");
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    private static WorkflowOrgGrant Grant(string resourceOrganizationId, string? resourceType = "ServiceOrder", params string[] actions) =>
        new(resourceOrganizationId, resourceType, actions.Length == 0 ? ["serviceorders:view"] : actions, RequiredRole: null);

    private static WorkflowPrincipal Principal(params WorkflowOrgGrant[] grants) =>
        new("user-1", Own.ToString(), [], [], []) { OrgGrants = grants };

    private static ServiceOrder Order(string id, Guid owner, int daysAgo) => new()
    {
        Id = id,
        Title = id,
        OrderTypeId = "survey",
        Status = ServiceOrderStatus.Pending,
        CreatedBy = "creator",
        OrganizationId = owner,
        CreatedAt = Now.AddDays(-daysAgo),
    };

    private static ServiceOrderRules Rules()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "survey", DisplayName = "Survey" });
        return new ServiceOrderRules(registry, timeProvider: new FakeTimeProvider(new DateTimeOffset(Now)));
    }

    private sealed class Reader(params IServiceOrder[] orders) : ICrossOrganizationServiceOrderReader
    {
        public List<IReadOnlyCollection<Guid>> Calls { get; } = [];

        public Task<IReadOnlyList<IServiceOrder>> GetOfOtherOrganizationsAsync(IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default)
        {
            Calls.Add(organizationIds);
            return Task.FromResult<IReadOnlyList<IServiceOrder>>([.. orders.Where(o => organizationIds.Contains(o.OrganizationId))]);
        }
    }

    // ── GrantingOrganizations ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GrantingOrganizations_AreTheOrganizationsOfServiceOrderGrants_InAnySpelling_Once()
    {
        var principal = Principal(
            Grant(Granting.ToString()),
            Grant(Granting.ToString("N").ToUpperInvariant(), resourceType: null, "SERVICEORDERS:EXECUTE"));

        GrantedServiceOrderList.GrantingOrganizations(principal).Should().BeEquivalentTo([Granting]);
    }

    [Fact]
    public void GrantingOrganizations_LeaveOut_OtherResourceTypes_GrantsWithNoServiceOrderAction_TheOwnOrganization_AndNonIds()
    {
        var principal = Principal(
            Grant(Other.ToString(), resourceType: "Feature", "features:read"),
            Grant(Other.ToString(), resourceType: null, "features:read"),
            Grant(Other.ToString(), resourceType: "ServiceOrder", "features:read"),
            Grant(Own.ToString()),
            Grant(Guid.Empty.ToString()),
            Grant("not-a-guid"));

        GrantedServiceOrderList.GrantingOrganizations(principal).Should().BeEmpty();
    }

    [Fact]
    public void GrantingOrganizations_OfAPrincipalWithNoGrants_AreNone_AndANullPrincipalThrows()
    {
        GrantedServiceOrderList.GrantingOrganizations(Principal()).Should().BeEmpty();
        ((Action)(() => GrantedServiceOrderList.GrantingOrganizations(null!))).Should().Throw<ArgumentNullException>();
    }

    // ── ComposeAsync ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compose_AddsTheGrantedOrders_OrdersByCreation_AndListsAnOrderOnce()
    {
        var oldOwn = Order("own-old", Own, daysAgo: 3);
        var newOwn = Order("own-new", Own, daysAgo: 1);
        var granted = Order("granted", Granting, daysAgo: 2);
        var reader = new Reader(granted, Order("own-new", Own, daysAgo: 1));

        var listed = await GrantedServiceOrderList.ComposeAsync([oldOwn, newOwn], reader, Rules(), Principal(Grant(Granting.ToString())), CancellationToken.None);

        listed.Select(o => o.Id).Should().Equal("own-old", "granted", "own-new");
        listed[2].Should().BeSameAs(newOwn, "the caller's own copy is the one kept");
        reader.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo([Granting]);
    }

    [Fact]
    public async Task Compose_KeepsOnlyOrdersThePrincipalPassesTheGateOn()
    {
        var foreign = Order("foreign", Other, daysAgo: 1);
        var grant = Grant(Granting.ToString(), "ServiceOrder", "serviceorders:view");
        var reader = new Reader(Order("granted", Granting, daysAgo: 2));

        var listed = await GrantedServiceOrderList.ComposeAsync([foreign], reader, Rules(), Principal(grant), CancellationToken.None);

        listed.Select(o => o.Id).Should().Equal("granted");
    }

    [Fact]
    public async Task Compose_WithoutAReader_OrWithoutAGrant_IsTheGatedOwnList_AndAsksNothing()
    {
        var own = Order("own", Own, daysAgo: 1);
        var foreign = Order("foreign", Other, daysAgo: 2);
        var reader = new Reader(Order("granted", Granting, daysAgo: 2));

        (await GrantedServiceOrderList.ComposeAsync([own, foreign], null, Rules(), Principal(Grant(Granting.ToString())), CancellationToken.None))
            .Should().Equal(own);
        (await GrantedServiceOrderList.ComposeAsync([own, foreign], reader, Rules(), Principal(), CancellationToken.None))
            .Should().Equal(own);
        reader.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Compose_NullArguments_Throw()
    {
        var principal = Principal();

        await ((Func<Task>)(() => GrantedServiceOrderList.ComposeAsync(null!, null, Rules(), principal, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => GrantedServiceOrderList.ComposeAsync([], null, null!, principal, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => GrantedServiceOrderList.ComposeAsync([], null, Rules(), null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
    }
}
