using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;
using static GeoAssets.Server.Tests.SharedUseApiSeed;

namespace GeoAssets.Server.Tests;

/// <summary>
/// What the organization a shared-use order was dispatched to can do through the REST surface
/// (XD01-316): list and open the order dispatched to it and read its targets — and nothing
/// more, the notice type being one that only informs its recipient (XD01-333). It does not see
/// the parent or another occupant's order, it cannot change the order or a target, it cannot
/// read or stage occupancy, and it cannot dispatch. Order <c>ord-1</c> of organization A
/// is dispatched to organizations B and C before each fact.
/// </summary>
public sealed class ServiceOrderDispatchRecipientEndpointTests
{
    private static readonly string ChildOfC = ChildOf(OrderTargetsApiHost.OrgC);
    private static readonly string ChildOfB = ChildOf(OrderTargetsApiHost.OrgB);

    private static async Task<OrderTargetsApiHost> DispatchedAsync()
    {
        var host = await StartAsync(mapOccupancyRoutes: true);
        using var owner = host.ClientFor("admin");
        await (await DispatchAsync(owner)).ReadJsonAsync();
        return host;
    }

    private static async Task<List<string>> OrderIdsAsync(HttpClient client, string path)
    {
        var body = await (await client.GetAsync(Path(path))).ReadJsonAsync();
        return [.. body.EnumerateArray().Select(order => order.GetProperty("id").GetString()!).Order(StringComparer.Ordinal)];
    }

    // ── List and open ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ARecipient_ListsTheOrdersDispatchedToItsOrganization_AndOnlyThose()
    {
        await using var host = await DispatchedAsync();
        using var occupant = host.ClientFor("occupant");

        var inbox = await OrderIdsAsync(occupant, $"service-orders/dispatched-to/{OrderTargetsApiHost.OrgC}?targetType=Organization");

        inbox.Should().Equal(ChildOfC);
    }

    [Fact]
    public async Task ARecipient_NamingAnotherOrganizationInTheUrl_GetsNothingOfThatOrganization()
    {
        await using var host = await DispatchedAsync();
        using var occupant = host.ClientFor("occupant");

        var othersInbox = await OrderIdsAsync(occupant, $"service-orders/dispatched-to/{OrderTargetsApiHost.OrgB}?targetType=Organization");
        var everything = await OrderIdsAsync(occupant, "service-orders");
        var children = await OrderIdsAsync(occupant, $"service-orders/{Order}/children");

        othersInbox.Should().BeEmpty("the id in the URL selects among what the caller may see; it never widens it");
        everything.Should().Equal([ChildOfC], "the organization gate keeps every list to the orders the caller reaches");
        children.Should().Equal([ChildOfC], "of the parent's children it sees its own");
    }

    [Fact]
    public async Task ARecipient_OpensTheOrderDispatchedToIt_WithItsDispatch_AndGets404ForTheParentAndForAnotherOccupantsOrder()
    {
        await using var host = await DispatchedAsync();
        using var occupant = host.ClientFor("occupant");

        var opened = await (await occupant.GetAsync(Path($"service-orders/{ChildOfC}"))).ReadJsonAsync();
        var parent = await occupant.GetAsync(Path($"service-orders/{Order}"));
        var othersChild = await occupant.GetAsync(Path($"service-orders/{ChildOfB}"));
        var parentOfChild = await occupant.GetAsync(Path($"service-orders/{ChildOfC}/parent"));

        opened.GetProperty("organizationId").GetGuid().Should().Be(OrderTargetsApiHost.OrgA, "the order stays the pole owner's");
        opened.GetProperty("parentOrderId").GetString().Should().Be(Order);
        opened.GetProperty("dispatches").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("targetId").GetString().Should().Be(OrderTargetsApiHost.OrgC.ToString());
        parent.StatusCode.Should().Be(HttpStatusCode.NotFound);
        othersChild.StatusCode.Should().Be(HttpStatusCode.NotFound);
        parentOfChild.StatusCode.Should().Be(HttpStatusCode.NotFound, "reaching a child gives no way up to its parent");
    }

    [Fact]
    public async Task ThePoleOwner_StillSeesTheParentAndEveryChild()
    {
        await using var host = await DispatchedAsync();
        using var owner = host.ClientFor("reader");

        (await OrderIdsAsync(owner, $"service-orders/{Order}/children")).Should().BeEquivalentTo([ChildOfB, ChildOfC]);
        (await OrderIdsAsync(owner, $"service-orders/dispatched-to/{OrderTargetsApiHost.OrgC}?targetType=Organization")).Should().Equal(ChildOfC);
        (await owner.GetAsync(Path($"service-orders/{Order}"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Targets: read-only ────────────────────────────────────────────────────

    [Fact]
    public async Task ARecipient_ReadsTheTargetsOfItsOrder_WithTheActivitiesThePoleOwnerAssigned()
    {
        await using var host = await DispatchedAsync();
        using var occupant = host.ClientFor("occupant");

        var page = await (await occupant.GetAsync(Path($"service-orders/{ChildOfC}/targets"))).ReadJsonAsync();
        var count = await (await occupant.GetAsync(Path($"service-orders/{ChildOfC}/targets/count"))).ReadJsonAsync();
        var parentTargets = await occupant.GetAsync(Path($"service-orders/{Order}/targets"));

        page.GetProperty("totalCount").GetInt32().Should().Be(2);
        page.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("featureId").GetString()).Should().Equal(Pole(1), Pole(2));
        page.GetProperty("items").EnumerateArray().Should().OnlyContain(t => t.GetProperty("activityIds").Strings().SequenceEqual(new[] { Activity }));
        count.GetProperty("count").GetInt32().Should().Be(2);
        await parentTargets.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
    }

    [Fact]
    public async Task ARecipient_CanNeitherEditNorRemoveNorAddATargetOfItsOrder_ItGets403WithTheReadOnlyReason()
    {
        await using var host = await DispatchedAsync();
        using var occupant = host.ClientFor("occupant");
        var target = host.Store.Snapshot().First(t => string.Equals(t.OrderId, ChildOfC, StringComparison.Ordinal));
        var before = host.Store.Snapshot().Count(t => string.Equals(t.OrderId, ChildOfC, StringComparison.Ordinal));

        var patch = await occupant.PatchRawAsync(
            $"service-orders/{ChildOfC}/targets/{target.Id}", $$"""{"status":"InProgress","note":"ours now","version":{{target.Version}}}""");
        var remove = await occupant.PostJsonAsync($"service-orders/{ChildOfC}/targets/remove", new { targetIds = new[] { target.Id } });
        var apply = await occupant.PostJsonAsync($"service-orders/{ChildOfC}/targets/selection/apply", FenceBody());

        foreach (var response in new[] { patch, remove, apply })
        {
            var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
            problem.GetProperty("reason").GetString().Should().Be(ServiceOrderRules.RecipientTargetsAreReadOnlyReason);
        }

        host.Store.Snapshot().Count(t => string.Equals(t.OrderId, ChildOfC, StringComparison.Ordinal)).Should().Be(before);
        host.Store.Snapshot().Single(t => string.Equals(t.Id, target.Id, StringComparison.Ordinal)).Should().BeEquivalentTo(target);
    }

    [Fact]
    public async Task ThePoleOwner_StillEditsTheTargetsOfAChildOrder()
    {
        await using var host = await DispatchedAsync();
        using var owner = host.ClientFor("admin");
        var target = host.Store.Snapshot().First(t => string.Equals(t.OrderId, ChildOfC, StringComparison.Ordinal));

        var patch = await owner.PatchRawAsync(
            $"service-orders/{ChildOfC}/targets/{target.Id}", $$"""{"note":"mind the dog","version":{{target.Version}}}""");

        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
        host.Store.Snapshot().Single(t => string.Equals(t.Id, target.Id, StringComparison.Ordinal)).Note.Should().Be("mind the dog");
    }

    // ── What a recipient may still do, and what stays the owner's ─────────────

    [Fact]
    public async Task ARecipient_CanNeitherAcceptNorCancelItsNotice_ThroughTheActionRoute_TheNoticeOnlyInformsIt()
    {
        await using var host = await DispatchedAsync();
        using var occupant = host.ClientFor("occupant");
        var actor = OrderTargetsApiHost.Identities["occupant"].UserId.ToString();

        var accept = await occupant.PostJsonAsync(
            $"service-orders/{ChildOfC}/actions", new { action = (int)OrderActionType.Accept, performedBy = actor, performedAt = OrderTargetsApiHost.Now.UtcDateTime });
        var cancel = await occupant.PostJsonAsync(
            $"service-orders/{ChildOfC}/actions",
            new { action = (int)OrderActionType.Cancel, performedBy = actor, performedAt = OrderTargetsApiHost.Now.UtcDateTime, resultingStatus = ServiceOrderStatus.Cancelled });

        // XD01-333: the occupant-notice type is Inform, so its recipient reads it and changes nothing —
        // not even with the Supervisor role that grants Cancel inside its own organization.
        foreach (var response in new[] { accept, cancel })
        {
            (await response.ReadJsonAsync(HttpStatusCode.Forbidden)).GetProperty("reason").GetString()
                .Should().Be(ServiceOrderRules.RecipientOrderIsReadOnlyReason);
        }

        var stored = await host.Orders.GetByIdAsync(ChildOfC);
        stored!.ActionLog.Should().BeEmpty();
        stored.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public async Task ARecipient_CanNeitherReadNorStageTheOccupancyOfThePolesOfItsOrder()
    {
        await using var host = await DispatchedAsync();
        using var occupant = host.ClientFor("occupant");
        var target = host.Store.Snapshot().First(t => string.Equals(t.OrderId, ChildOfC, StringComparison.Ordinal));

        var read = await occupant.GetAsync(Path($"service-orders/{ChildOfC}/targets/{target.Id}/occupancy"));
        var list = await occupant.GetAsync(Path($"service-orders/{ChildOfC}/occupancy-changes"));
        var stage = await occupant.PutAsync(
            Path($"service-orders/{ChildOfC}/targets/{target.Id}/occupancy-changes"),
            new StringContent($$"""{"changes":[{"kind":{{(int)OccupancyChangeKind.Remove}},"slotIndex":1}]}""", System.Text.Encoding.UTF8, "application/json"));

        foreach (var response in new[] { read, list, stage })
            await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task ARecipient_CannotDispatchItsOrderOnwards_NotAsAFanOut_AndTheSharedUseRoutesOfTheParentAre404ForIt()
    {
        await using var host = await DispatchedAsync();
        using var occupant = host.ClientFor("occupant");

        var fanOutOfItsOwn = await DispatchAsync(occupant, ChildOfC);
        var planOfTheParent = await occupant.GetAsync(Path($"service-orders/{Order}/shared-use/dispatch-plan"));

        await fanOutOfItsOwn.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        await planOfTheParent.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
    }

    [Fact]
    public async Task AnOrganizationThatWasNotDispatchedTo_SeesNoneOfIt()
    {
        await using var host = await StartAsync(poles: 1);
        using (var owner = host.ClientFor("admin"))
            await (await DispatchAsync(owner)).ReadJsonAsync();
        using var notARecipient = host.ClientFor("admin-b");

        (await OrderIdsAsync(notARecipient, "service-orders")).Should().BeEmpty("organization B occupies only the second pole, which this order does not hold");
        (await notARecipient.GetAsync(Path($"service-orders/{ChildOfC}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await (await notARecipient.GetAsync(Path($"service-orders/{ChildOfC}/targets")))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
    }
}
