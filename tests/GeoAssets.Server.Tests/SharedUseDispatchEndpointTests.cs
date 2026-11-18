using System.Net;
using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;
using static GeoAssets.Server.Tests.SharedUseApiSeed;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The shared-use dispatch routes (XD01-316) through the real <c>MapSharedUseDispatchApi()</c>
/// in a TestServer: the real <see cref="SharedUseDispatchService"/>, the production
/// authorization pipeline, and the TestKit's in-memory stores scoped by the request's token.
/// Order <c>ord-1</c> of organization A holds two poles; organization C occupies both and
/// organization B the second.
/// </summary>
public sealed class SharedUseDispatchEndpointTests
{
    private const string PlanRoute = $"service-orders/{Order}/shared-use/dispatch-plan";

    public static TheoryData<string> ReadRoutes() => ["plan", "skipped-entries", "unnotifiable-poles"];

    public static TheoryData<string> Routes() => ["plan", "skipped-entries", "unnotifiable-poles", "dispatch"];

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string route, string orderId = Order, string query = "") => route switch
    {
        "plan" => client.GetAsync(Path($"service-orders/{orderId}/shared-use/dispatch-plan{query}")),
        "skipped-entries" => client.GetAsync(Path($"service-orders/{orderId}/shared-use/dispatch-plan/skipped-entries{query}")),
        "unnotifiable-poles" => client.GetAsync(Path($"service-orders/{orderId}/shared-use/dispatch-plan/unnotifiable-poles{query}")),
        "dispatch" => DispatchAsync(client, orderId),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown route."),
    };

    // ── GET dispatch-plan ─────────────────────────────────────────────────────

    [Fact]
    public async Task Plan_ReturnsTheCountsAndOnePageOfRecipients_AndWritesNothing()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("admin");

        var body = await (await client.GetAsync(Path(PlanRoute))).ReadJsonAsync();

        body.GetProperty("orderId").GetString().Should().Be(Order);
        body.GetProperty("orderTypeId").GetString().Should().Be(SharedUseOrderTypes.PoleWorkId);
        body.GetProperty("childOrderTypeId").GetString().Should().Be(SharedUseOrderTypes.OccupantNoticeId);
        body.GetProperty("statusAfterDispatch").GetString().Should().Be(ServiceOrderStatus.Pending);
        body.GetProperty("rowVersion").GetString().Should().Be(string.Empty, "the fake store gives orders no row version; the wire carries it as base64");
        body.GetProperty("targetCount").GetInt32().Should().Be(2);
        body.GetProperty("blockedTargetCount").GetInt32().Should().Be(0);
        body.GetProperty("targetsWithoutActivityCount").GetInt32().Should().Be(0);
        body.GetProperty("unnotifiablePoleCount").GetInt32().Should().Be(0);
        body.GetProperty("skippedEntryCount").GetInt32().Should().Be(0);
        body.GetProperty("newRecipientCount").GetInt32().Should().Be(2);
        body.GetProperty("canDispatch").GetBoolean().Should().BeTrue();
        body.TryGetProperty("blockReason", out _).Should().BeFalse("nothing blocks the dispatch");
        var recipients = body.GetProperty("recipients");
        recipients.GetProperty("totalCount").GetInt32().Should().Be(2);
        var items = recipients.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("occupantOrganizationId").GetGuid().Should().Be(OrderTargetsApiHost.OrgB, "recipients come in organization order");
        items[0].GetProperty("poleCount").GetInt32().Should().Be(1);
        items[0].GetProperty("childOrderId").GetString().Should().Be(ChildOf(OrderTargetsApiHost.OrgB));
        items[0].GetProperty("alreadyDispatched").GetBoolean().Should().BeFalse();
        items[1].GetProperty("occupantOrganizationId").GetGuid().Should().Be(OrderTargetsApiHost.OrgC);
        items[1].GetProperty("poleCount").GetInt32().Should().Be(2);

        (await host.Orders.GetChildrenAsync(Order)).Should().BeEmpty();
        host.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_PagesItsRecipients()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("admin");

        var second = await (await client.GetAsync(Path($"{PlanRoute}?page=2&pageSize=1"))).ReadJsonAsync();

        second.GetProperty("recipients").GetProperty("totalCount").GetInt32().Should().Be(2);
        var item = second.GetProperty("recipients").GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("occupantOrganizationId").GetGuid().Should().Be(OrderTargetsApiHost.OrgC);
    }

    [Fact]
    public async Task Plan_ForAnOrderThatCannotBeDispatched_Is200_WithTheReason()
    {
        await using var host = await StartAsync(occupied: false);
        using var client = host.ClientFor("admin");

        var body = await (await client.GetAsync(Path(PlanRoute))).ReadJsonAsync();

        body.GetProperty("blockReason").GetInt32().Should().Be((int)SharedUseDispatchBlockReason.NoEligibleOccupants);
        body.GetProperty("canDispatch").GetBoolean().Should().BeFalse();
        body.GetProperty("unnotifiablePoleCount").GetInt32().Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(ReadRoutes))]
    public async Task ReadRoutes_PagingOutOfRange_Return400PagingOutOfRange(string route)
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("admin");

        await (await SendAsync(client, route, query: "?page=0")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange);
        await (await SendAsync(client, route, query: "?pageSize=201")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange);
        await (await SendAsync(client, route, query: "?page=abc")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange);
    }

    // ── GET skipped-entries, unnotifiable-poles ───────────────────────────────

    [Fact]
    public async Task SkippedEntriesAndUnnotifiablePoles_ArePaged_AndSayWhyEachEntryGetsNoDispatch()
    {
        await using var host = await StartAsync(poles: 3, occupied: false);
        var clandestine = Entry(Pole(1), 2, OrderTargetsApiHost.OrgB, PoleAttachmentOccupantStatus.ClandestineUnauthorized);
        var unidentifiedBeyondSlots = Entry(Pole(1), slot: null, occupant: null);
        await OccupyAsync(host, Pole(1), Entry(Pole(1), 1, OrderTargetsApiHost.OrgC), clandestine, unidentifiedBeyondSlots);
        await OccupyAsync(host, Pole(2), Entry(Pole(2), 1, OrderTargetsApiHost.OrgA));
        using var client = host.ClientFor("admin");

        var skipped = await (await client.GetAsync(Path($"{PlanRoute}/skipped-entries?page=1&pageSize=2"))).ReadJsonAsync();
        var poles = await (await client.GetAsync(Path($"{PlanRoute}/unnotifiable-poles"))).ReadJsonAsync();

        skipped.GetProperty("page").GetInt32().Should().Be(1);
        skipped.GetProperty("pageSize").GetInt32().Should().Be(2);
        skipped.GetProperty("totalCount").GetInt32().Should().Be(3);
        var items = skipped.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items.Select(item => item.GetProperty("poleFeatureId").GetString()).Should().AllBe(Pole(1));
        var byEntry = items.ToDictionary(item => item.GetProperty("entryId").GetGuid());
        byEntry[clandestine.Id].GetProperty("reason").GetInt32().Should().Be((int)SharedUseSkipReason.NoActiveContract);
        byEntry[clandestine.Id].GetProperty("slotIndex").GetInt32().Should().Be(2);
        byEntry[clandestine.Id].GetProperty("status").GetInt32().Should().Be((int)PoleAttachmentOccupantStatus.ClandestineUnauthorized);
        byEntry[unidentifiedBeyondSlots.Id].GetProperty("reason").GetInt32().Should().Be((int)SharedUseSkipReason.UnknownOrganization);
        byEntry[unidentifiedBeyondSlots.Id].TryGetProperty("slotIndex", out _).Should().BeFalse("an entry beyond the regulated slots has no slot");
        items.SelectMany(item => item.EnumerateObject().Select(member => member.Name)).Distinct(StringComparer.Ordinal)
            .Should().BeSubsetOf(["poleFeatureId", "entryId", "slotIndex", "status", "reason"], "a skipped entry says where and why, never who");

        poles.GetProperty("items").Strings().Should().Equal(Pole(2), Pole(3));
        poles.GetProperty("totalCount").GetInt32().Should().Be(2);
        poles.GetProperty("page").GetInt32().Should().Be(1);
        poles.GetProperty("pageSize").GetInt32().Should().Be(50);
    }

    // ── POST dispatch ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_CreatesOneOrderPerOccupant_StagesTheirNotifications_AndAnswersWithTheCounts()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("admin");

        var body = await (await DispatchAsync(client)).ReadJsonAsync();

        body.GetProperty("parentOrderId").GetString().Should().Be(Order);
        body.GetProperty("orderTypeId").GetString().Should().Be(SharedUseOrderTypes.PoleWorkId);
        body.GetProperty("parentStatus").GetString().Should().Be(ServiceOrderStatus.Pending);
        body.GetProperty("childOrdersCreated").GetInt32().Should().Be(2);
        body.GetProperty("childOrdersAlreadyDispatched").GetInt32().Should().Be(0);
        body.GetProperty("notificationsEnqueued").GetInt32().Should().Be(2);
        body.GetProperty("targetCount").GetInt32().Should().Be(2);

        var children = await host.Orders.GetChildrenAsync(Order);
        children.Select(c => c.Id).Should().BeEquivalentTo([ChildOf(OrderTargetsApiHost.OrgB), ChildOf(OrderTargetsApiHost.OrgC)]);
        children.Should().OnlyContain(c => c.OrganizationId == OrderTargetsApiHost.OrgA && c.OrderTypeId == SharedUseOrderTypes.OccupantNoticeId);
        children.Should().OnlyContain(
            c => c.CreatedBy == OrderTargetsApiHost.Identities["admin"].UserId.ToString(), "the dispatcher is whoever the token names");
        (await host.Orders.GetByIdAsync(Order))!.Status.Should().Be(ServiceOrderStatus.Pending);
        host.Store.Snapshot().Where(t => string.Equals(t.OrderId, ChildOf(OrderTargetsApiHost.OrgC), StringComparison.Ordinal)).Select(t => t.FeatureId)
            .Should().BeEquivalentTo([Pole(1), Pole(2)]);
        host.Outbox.Messages.Select(m => ((OrderStateChangedEvent)m.Message).OrderId)
            .Should().BeEquivalentTo([ChildOf(OrderTargetsApiHost.OrgB), ChildOf(OrderTargetsApiHost.OrgC)]);
        host.Outbox.Messages.Should().OnlyContain(m => m.CorrelationId == Order);
    }

    [Fact]
    public async Task Dispatch_Repeated_Is200_CreatesNothing_AndThePlanThenShowsEveryRecipientAsDispatched()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("admin");
        await (await DispatchAsync(client)).ReadJsonAsync();

        var again = await (await DispatchAsync(client)).ReadJsonAsync();
        var plan = await (await client.GetAsync(Path(PlanRoute))).ReadJsonAsync();

        again.GetProperty("childOrdersCreated").GetInt32().Should().Be(0);
        again.GetProperty("childOrdersAlreadyDispatched").GetInt32().Should().Be(2);
        again.GetProperty("notificationsEnqueued").GetInt32().Should().Be(0);
        host.Outbox.Messages.Should().HaveCount(2);
        plan.GetProperty("newRecipientCount").GetInt32().Should().Be(0);
        plan.GetProperty("canDispatch").GetBoolean().Should().BeFalse("there is nobody left to dispatch to");
        plan.GetProperty("recipients").GetProperty("items").EnumerateArray()
            .Should().OnlyContain(r => r.GetProperty("alreadyDispatched").GetBoolean());
    }

    [Fact]
    public async Task Dispatch_IgnoresAnyCallerNamedInTheBody_TheDispatcherIsTheTokens()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync(
            $"service-orders/{Order}/shared-use/dispatch",
            new { rowVersion = string.Empty, userId = "someone-else", principal = new { userId = "someone-else", roleNames = new[] { "Administrator" } } });

        await response.ReadJsonAsync();
        (await host.Orders.GetChildrenAsync(Order)).Should().OnlyContain(
            c => c.CreatedBy == OrderTargetsApiHost.Identities["supervisor-a"].UserId.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"rowVersion":null}""")]
    [InlineData("""{"rowVersion":"***not base64***"}""")]
    public async Task Dispatch_BodyMissingMalformedOrWithoutARowVersion_Returns400ValidationFailed_AndDispatchesNothing(string body)
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("admin");

        var response = await client.PostRawAsync($"service-orders/{Order}/shared-use/dispatch", body);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
        (await host.Orders.GetChildrenAsync(Order)).Should().BeEmpty();
    }

    // ── Who may ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_WithoutAToken_Returns401(string route)
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor(token: null);

        (await SendAsync(client, route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.Orders.GetChildrenAsync(Order)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ReadRoutes))]
    public async Task ReadRoutes_WithoutTheReadPermission_Return403(string route)
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("no-permission");

        (await SendAsync(client, route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_UnknownOrder_Returns404OrdersNotFound(string route)
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("admin");

        var problem = await (await SendAsync(client, route, "ord-missing")).ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);

        problem.GetProperty("orderId").GetString().Should().Be("ord-missing");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_ByAnAdministratorOfAnotherOrganization_AnswersExactlyAsForAnUnknownOrder(string route)
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("admin-b");

        var refused = await (await SendAsync(client, route)).ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        var unknown = await (await SendAsync(client, route, "ord-missing")).ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);

        refused.GetRawText().Replace(Order, "ord-missing", StringComparison.Ordinal).Should().Be(
            unknown.GetRawText(), "a refusal must not confirm that the order exists");
        (await host.Orders.GetChildrenAsync(Order)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_ByAMemberWithoutTheRightToDispatch_Returns403AuthDenied(string route)
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("reader");

        var problem = await (await SendAsync(client, route)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);

        problem.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        (await host.Orders.GetChildrenAsync(Order)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_ByAGranteeAllowedToDispatch_Returns403_BecauseTheDispatchIsThePoleOwnersAlone(string route)
    {
        await using var host = await StartAsync();
        host.Grants.Active.Add(new OrganizationGrant
        {
            Id = Guid.NewGuid(),
            GranteeOrganizationId = OrderTargetsApiHost.OrgB,
            ResourceOrganizationId = OrderTargetsApiHost.OrgA,
            ResourceType = nameof(ServiceOrder),
            AllowedActions = ["serviceorders:view", "serviceorders:dispatch"],
            GrantedBy = "tester",
            GrantedAt = OrderTargetsApiHost.Now.UtcDateTime,
            IsActive = true,
        });
        using var client = host.ClientFor("admin-b");

        var problem = await (await SendAsync(client, route)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);

        problem.GetProperty("reason").GetString().Should().Contain("organization that owns its poles");
        (await host.Orders.GetChildrenAsync(Order)).Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_ByASupervisorOfThePoleOwner_IsAllowed()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("supervisor-a");

        var body = await (await DispatchAsync(client)).ReadJsonAsync();

        body.GetProperty("childOrdersCreated").GetInt32().Should().Be(2);
    }

    // ── Orders that cannot be dispatched ──────────────────────────────────────

    [Fact]
    public async Task Dispatch_OrderOfATypeThatIsNotRegistered_Returns404OrderTypesNotFound()
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync("ord-ghost", "ghost-type");
        using var client = host.ClientFor("admin");

        var problem = await (await DispatchAsync(client, "ord-ghost")).ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderTypeNotFound);

        problem.GetProperty("orderTypeId").GetString().Should().Be("ghost-type");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_OrderWhoseTypeHasNoFanOut_Returns422OrdersNoFanOut(string route)
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync("ord-survey", OrderTargetsApiHost.PoleSurvey);
        using var client = host.ClientFor("admin");

        var problem = await (await SendAsync(client, route, "ord-survey")).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, SharedUseProblemCodes.NoFanOut);

        problem.GetProperty("orderTypeId").GetString().Should().Be(OrderTargetsApiHost.PoleSurvey);
    }

    [Fact]
    public async Task Dispatch_OrderWithNoTargets_Returns422DispatchNoTargets()
    {
        await using var host = await StartAsync(poles: 0);
        using var client = host.ClientFor("admin");

        var problem = await (await DispatchAsync(client)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, SharedUseProblemCodes.NoTargets);

        problem.TryGetProperty("targetIds", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Dispatch_TargetWithoutAnActivity_Returns422_NamingTheTargets_AndDispatchesNothing()
    {
        await using var host = await StartAsync();
        await SeedTargetsAsync(host, Order, [], Pole(7), Pole(8));
        using var client = host.ClientFor("admin");

        var problem = await (await DispatchAsync(client)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, SharedUseProblemCodes.TargetWithoutActivity);

        problem.GetProperty("targetCount").GetInt32().Should().Be(2);
        problem.GetProperty("targetIds").Strings().Should().Equal($"{Order}-{Pole(7)}", $"{Order}-{Pole(8)}");
        (await host.Orders.GetChildrenAsync(Order)).Should().BeEmpty();
        (await host.Orders.GetByIdAsync(Order))!.Status.Should().Be(ServiceOrderStatus.Draft);
    }

    [Fact]
    public async Task Dispatch_NoOccupantUnderContract_Returns422DispatchNoOccupants()
    {
        await using var host = await StartAsync(occupied: false);
        using var client = host.ClientFor("admin");

        await (await DispatchAsync(client)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, SharedUseProblemCodes.NoOccupants);
    }

    [Fact]
    public async Task Dispatch_EveryTargetBlockedByAnotherOrder_Returns422DispatchAllTargetsBlocked()
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync("ord-blocking", OrderTargetsApiHost.BlockingSurvey);
        await SeedTargetsAsync(host, "ord-blocking", [], Pole(1), Pole(2));
        using var client = host.ClientFor("admin");

        await (await DispatchAsync(client)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, SharedUseProblemCodes.AllTargetsBlocked);
    }

    [Theory]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Completed)]
    public async Task EveryRoute_OrderInAStatusWithNoDispatch_Returns409OrdersInvalidState(string status)
    {
        await using var host = await StartAsync(status);
        using var client = host.ClientFor("admin");

        foreach (var route in new[] { "plan", "dispatch" })
        {
            var problem = await (await SendAsync(client, route)).ShouldBeProblemAsync(HttpStatusCode.Conflict, WorkflowProblemCodes.OrderInvalidState);
            problem.GetProperty("from").GetString().Should().Be(status);
        }
    }

    [Fact]
    public async Task Dispatch_OrderChangedSinceTheCallerReadIt_Returns409ConcurrentModification()
    {
        await using var host = await StartAsync(decorateOrders: inner => new StaleOnUpdateOrders(inner));
        using var client = host.ClientFor("admin");

        var problem = await (await DispatchAsync(client)).ShouldBeProblemAsync(HttpStatusCode.Conflict, WorkflowProblemCodes.TargetConcurrentModification);

        problem.GetProperty("orderId").GetString().Should().Be(Order);
        (await host.Orders.GetChildrenAsync(Order)).Should().BeEmpty();
    }

    [Fact]
    public async Task EveryRoute_AFailureThatIsNotOneOfTheServicesOwn_IsNotDressedUpAsAProblem_ItReachesTheHost()
    {
        await using var host = await StartAsync(decorateOrders: inner => new StaleOnUpdateOrders(inner, _ => new TimeoutException("database")));
        using var client = host.ClientFor("admin");

        var act = async () => await DispatchAsync(client);

        await act.Should().ThrowAsync<TimeoutException>("TestServer rethrows what production would answer with a 500");
    }

    [Fact]
    public void MapSharedUseDispatchApi_NullRoutes_ThrowsArgumentNullException() =>
        ((Action)(() => SharedUseRestApiExtensions.MapSharedUseDispatchApi(null!))).Should().Throw<ArgumentNullException>();

    /// <summary>An order store whose every update fails — by default as a write lost to another writer.</summary>
    private sealed class StaleOnUpdateOrders(IServiceOrderRepository inner, Func<string, Exception>? failure = null) : IServiceOrderRepository
    {
        public event EventHandler<IServiceOrder>? OrderAdded { add => inner.OrderAdded += value; remove => inner.OrderAdded -= value; }
        public event EventHandler<IServiceOrder>? OrderUpdated { add => inner.OrderUpdated += value; remove => inner.OrderUpdated -= value; }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add => inner.OrderStatusChanged += value; remove => inner.OrderStatusChanged -= value; }
        public event EventHandler<string>? OrderDeleted { add => inner.OrderDeleted += value; remove => inner.OrderDeleted -= value; }

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => inner.GetRootsAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => inner.GetChildrenAsync(parentId, ct);
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => inner.GetParentAsync(childId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => inner.GetByStatusAsync(status, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => inner.GetByAssigneeAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => inner.GetByCreatorAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => inner.GetByOrderTypeAsync(orderTypeId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => inner.GetByDateRangeAsync(from, toExclusive, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => inner.GetDispatchedToAsync(targetId, targetType, ct);
        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => inner.AddAsync(order, ct);
        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default) =>
            throw (failure is null ? new ServiceOrderConcurrencyException(order.Id) : failure(order.Id));
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => inner.AppendDispatchAsync(orderId, dispatch, ct);
        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default) => inner.AppendActionAsync(orderId, entry, ct);
        public Task DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
    }
}
