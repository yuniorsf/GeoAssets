using System.Net;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-332 — the organization gate on every order-target and selection route. A privileged
/// caller of another organization (token <c>admin-b</c>: Administrator of organization B, with
/// every route permission) gets, for an order of organization A, exactly the response it gets
/// for an order that does not exist, and nothing is read or written. Callers the gate lets
/// through — a member of the organization, a grantee, a dispatch recipient — are served as before.
/// </summary>
public class ServiceOrderTargetsOrganizationGateTests
{
    private const string PatchBody = """{ "note": "x", "version": 0 }""";

    private static async Task<OrderTargetsApiHost> HostAsync()
    {
        var host = await OrderTargetsApiHost.StartAsync();
        host.SeedAssets();
        await host.SeedOrderAsync();
        await host.SeedTargetsAsync("ord-1", 2);
        return host;
    }

    /// <summary>Every route addressed to one order, as a request for a given order id.</summary>
    public static TheoryData<string> OrderRoutes() =>
    [
        "GET targets", "GET count", "GET extent", "GET target", "POST preview", "POST apply", "PATCH target", "POST remove",
    ];

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string route, string orderId) => route switch
    {
        "GET targets" => client.GetAsync(Path($"service-orders/{orderId}/targets")),
        "GET count" => client.GetAsync(Path($"service-orders/{orderId}/targets/count")),
        "GET extent" => client.GetAsync(Path($"service-orders/{orderId}/targets/extent")),
        "GET target" => client.GetAsync(Path($"service-orders/{orderId}/targets/ord-1-t001")),
        "POST preview" => client.PostJsonAsync($"service-orders/{orderId}/targets/selection/preview", FenceBody()),
        "POST apply" => client.PostJsonAsync($"service-orders/{orderId}/targets/selection/apply", FenceBody()),
        "PATCH target" => client.PatchRawAsync($"service-orders/{orderId}/targets/ord-1-t001", PatchBody),
        "POST remove" => client.PostJsonAsync($"service-orders/{orderId}/targets/remove", new { targetIds = new[] { "ord-1-t001" } }),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown route."),
    };

    [Theory]
    [MemberData(nameof(OrderRoutes))]
    public async Task XD01_332_EveryOrderRoute_AdministratorOfAnotherOrganization_GetsTheSameResponseAsForAnUnknownOrder_AndNothingChanges(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin-b");

        var foreign = await SendAsync(client, route, "ord-1");
        var unknown = await SendAsync(client, route, "ord-0");

        var problem = await foreign.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        problem.TryGetProperty("reason", out _).Should().BeFalse();
        (await foreign.Content.ReadAsStringAsync()).Replace("ord-1", "ord-0", StringComparison.Ordinal)
            .Should().Be(await unknown.Content.ReadAsStringAsync());
        foreign.StatusCode.Should().Be(unknown.StatusCode);
        foreign.Content.Headers.ContentType.Should().Be(unknown.Content.Headers.ContentType);

        host.Store.Snapshot().Select(t => (t.Id, t.Note)).Should().BeEquivalentTo([("ord-1-t001", (string?)null), ("ord-1-t002", null)]);
        ((ServiceOrder)(await host.Orders.GetByIdAsync("ord-1"))!).ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task XD01_332_Usages_AdministratorOfAnotherOrganization_ExcludingAForeignOrder_GetsTheSameResponseAsForAnUnknownOrder()
    {
        await using var host = await HostAsync();
        await host.SeedOrderAsync("ord-other", status: ServiceOrderStatus.InProgress);
        await host.SeedTargetsAsync("ord-other", 1);
        using var client = host.ClientFor("admin-b");

        var foreign = await client.PostJsonAsync("service-orders/targets/usages", new { featureIds = new[] { "f-001" }, excludingOrderId = "ord-1" });
        var unknown = await client.PostJsonAsync("service-orders/targets/usages", new { featureIds = new[] { "f-001" }, excludingOrderId = "ord-0" });

        var body = await foreign.ReadJsonAsync();
        body.GetProperty("totalCount").GetInt32().Should().Be(0);
        (await foreign.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(OrderRoutes))]
    public async Task XD01_332_EveryOrderRoute_AdministratorOfTheOrdersOwnOrganization_IsServedAsBefore(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var response = await SendAsync(client, route, "ord-1");

        ((int)response.StatusCode).Should().BeInRange(200, 299, "the body was: {0}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(OrderRoutes))]
    public async Task XD01_332_EveryOrderRoute_MemberOfTheOrdersOrganizationWithoutARole_KeepsItsExistingAnswer_ReadsAllowedEditsForbidden(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        using var client = host.ClientFor("creator"); // organization A, no role, not this order's creator

        var response = await SendAsync(client, route, "ord-1");

        // Reads: the member shortcut. Preview needs View and edits need Annotate from the rule
        // chain, which grants this caller neither — the existing 403, not the gate's 404.
        if (route.StartsWith("GET", StringComparison.Ordinal))
            ((int)response.StatusCode).Should().BeInRange(200, 299);
        else
            (await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied))
                .GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(OrderRoutes))]
    public async Task XD01_332_EveryOrderRoute_Grantee_IsServedForTheActionsItsGrantLists(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        host.Grants.Active.Add(new()
        {
            GranteeOrganizationId = OrderTargetsApiHost.OrgB,
            ResourceOrganizationId = OrderTargetsApiHost.OrgA,
            ResourceType = "ServiceOrder",
            AllowedActions = { "serviceorders:view", "serviceorders:annotate" },
        });
        using var client = host.ClientFor("outsider");

        var response = await SendAsync(client, route, "ord-1");

        ((int)response.StatusCode).Should().BeInRange(200, 299, "the body was: {0}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("wrong resource type")]
    [InlineData("wrong scope")]
    [InlineData("another resource organization")]
    [InlineData("expired")]
    public async Task XD01_332_Grantee_WhoseGrantDoesNotCoverTheOrder_IsRefusedLikeAnyOutsider(string defect)
    {
        ArgumentNullException.ThrowIfNull(defect);
        await using var host = await HostAsync();
        host.Grants.Active.Add(new()
        {
            GranteeOrganizationId = OrderTargetsApiHost.OrgB,
            ResourceOrganizationId = string.Equals(defect, "another resource organization", StringComparison.Ordinal) ? Guid.NewGuid() : OrderTargetsApiHost.OrgA,
            ResourceType = string.Equals(defect, "wrong resource type", StringComparison.Ordinal) ? "GeoFeature" : "ServiceOrder",
            ScopeId = string.Equals(defect, "wrong scope", StringComparison.Ordinal) ? "municipio-999" : null,
            // The store behind this host hands every row over, so an expired grant reaches the
            // principal and it is the rule engine's own clock check that refuses it.
            ExpiresAt = string.Equals(defect, "expired", StringComparison.Ordinal) ? OrderTargetsApiHost.Now.UtcDateTime.AddMinutes(-1) : null,
            AllowedActions = { "serviceorders:view", "serviceorders:annotate" },
        });
        using var client = host.ClientFor("admin-b");

        var read = await client.GetAsync(Path("service-orders/ord-1/targets/count"));
        var edit = await client.PostJsonAsync("service-orders/ord-1/targets/remove", new { targetIds = new[] { "ord-1-t001" } });

        await read.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        await edit.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        host.Store.Snapshot().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(DispatchTargetType.Organization)]
    [InlineData(DispatchTargetType.User)]
    public async Task XD01_332_DispatchRecipientOfAnotherOrganization_KeepsViewAndAnnotate_AndIsForbiddenNotHiddenBeyondThem(DispatchTargetType targetType)
    {
        await using var host = await HostAsync();
        var outsider = OrderTargetsApiHost.Identities["outsider"];
        var recipientId = targetType == DispatchTargetType.Organization ? OrderTargetsApiHost.OrgB.ToString() : outsider.UserId.ToString();
        await host.Orders.AppendDispatchAsync(
            "ord-1", new OrderDispatch(recipientId, targetType, "dispatcher", OrderTargetsApiHost.Now.UtcDateTime));
        using var client = host.ClientFor("outsider");

        var read = await client.GetAsync(Path("service-orders/ord-1/targets/count"));
        var edit = await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", PatchBody);

        (await read.ReadJsonAsync()).GetProperty("count").GetInt32().Should().Be(2);
        edit.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task XD01_332_OrderWithNoOrganization_StaysReachableByDefault()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.Orders.AddAsync(new ServiceOrder
        {
            Id = "ord-legacy",
            Title = "legacy",
            OrderTypeId = OrderTargetsApiHost.PoleSurvey,
            Status = ServiceOrderStatus.Draft,
            CreatedBy = "someone-else",
        });
        using var client = host.ClientFor("admin-b");

        var response = await client.GetAsync(Path("service-orders/ord-legacy/targets/count"));

        (await response.ReadJsonAsync()).GetProperty("count").GetInt32().Should().Be(0);
    }
}
