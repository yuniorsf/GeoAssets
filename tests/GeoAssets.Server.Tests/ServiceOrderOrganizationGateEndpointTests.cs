using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-332 — the organization gate on every <c>MapServiceOrdersApi()</c> route, through the
/// production authorization pipeline. A privileged caller of another organization
/// (<c>admin-b</c>: Administrator of organization B holding every route permission) is answered,
/// for an order of organization A, exactly as for an order that does not exist — and sees none
/// of A's orders in any list. Members of the organization, grantees and dispatch recipients
/// keep the answers they had; an order's organization is set at creation and never changes.
/// </summary>
public class ServiceOrderOrganizationGateEndpointTests
{
    private static readonly Guid OrgA = OrderTargetsApiHost.OrgA;
    private static readonly Guid OrgB = OrderTargetsApiHost.OrgB;

    /// <summary>An order of A (<c>ord-1</c>, with child <c>ord-1-child</c>), and one of B (<c>ord-b</c>).</summary>
    private static async Task<OrderTargetsApiHost> HostAsync()
    {
        var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        await host.Orders.AddAsync(Order("ord-1", OrgA, assignedTo: "tech-a"));
        await host.Orders.AddAsync(Order("ord-1-child", OrgA, parentOrderId: "ord-1"));
        await host.Orders.AddAsync(Order("ord-b", OrgB));
        return host;
    }

    private static ServiceOrder Order(string id, Guid organizationId, string? assignedTo = null, string? parentOrderId = null, string title = "title") => new()
    {
        Id = id,
        Title = title,
        OrderTypeId = OrderTargetsApiHost.PoleSurvey,
        Status = ServiceOrderStatus.Draft,
        CreatedBy = "creator-a",
        AssignedTo = assignedTo,
        ParentOrderId = parentOrderId,
        OrganizationId = organizationId,
        CreatedAt = OrderTargetsApiHost.Now.UtcDateTime.AddDays(-1),
    };

    private static void GrantOrgB(OrderTargetsApiHost host, params string[] actions)
    {
        var grant = new GeoAssets.Identity.Authorization.Models.OrganizationGrant
        {
            GranteeOrganizationId = OrgB,
            ResourceOrganizationId = OrgA,
            ResourceType = "ServiceOrder",
        };
        grant.AllowedActions.AddRange(actions);
        host.Grants.Active.Add(grant);
    }

    private static async Task<string[]> IdsAsync(HttpResponseMessage response) =>
        [.. (await response.ReadJsonAsync()).EnumerateArray().Select(o => o.GetProperty("id").GetString()!)];

    private static async Task<ServiceOrder> StoredAsync(OrderTargetsApiHost host, string id) =>
        (ServiceOrder)(await host.Orders.GetByIdAsync(id))!;

    // ── Reads by id ─────────────────────────────────────────────────────────────

    public static TheoryData<string> SingleOrderWrites() => ["PUT", "POST dispatch", "POST actions", "DELETE"];

    private static Task<HttpResponseMessage> WriteAsync(HttpClient client, string route, string orderId) => route switch
    {
        "PUT" => client.PutAsJsonAsync(Path($"service-orders/{orderId}"), Order(orderId, OrgA, title: "edited")),
        "POST dispatch" => client.PostJsonAsync(
            $"service-orders/{orderId}/dispatch", new OrderDispatch("crew", DispatchTargetType.Group, "me", OrderTargetsApiHost.Now.UtcDateTime)),
        "POST actions" => client.PostJsonAsync(
            $"service-orders/{orderId}/actions", new OrderActionLog(OrderActionType.Annotate, "me", OrderTargetsApiHost.Now.UtcDateTime, "note")),
        "DELETE" => client.DeleteAsync(Path($"service-orders/{orderId}")),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown route."),
    };

    [Fact]
    public async Task XD01_332_GetById_AdministratorOfAnotherOrganization_GetsTheSame404AsForAnUnknownOrder()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin-b");

        var foreign = await client.GetAsync(Path("service-orders/ord-1"));
        var unknown = await client.GetAsync(Path("service-orders/ord-0"));
        var own = await client.GetAsync(Path("service-orders/ord-b"));

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
        (await own.ReadJsonAsync()).GetProperty("id").GetString().Should().Be("ord-b");
    }

    [Fact]
    public async Task XD01_332_GetParent_AdministratorOfAnotherOrganization_GetsTheSame404AsForAnUnknownOrder()
    {
        await using var host = await HostAsync();
        using var asOutsider = host.ClientFor("admin-b");
        using var asMember = host.ClientFor("reader");

        var foreign = await asOutsider.GetAsync(Path("service-orders/ord-1-child/parent"));
        var unknown = await asOutsider.GetAsync(Path("service-orders/ord-0/parent"));
        var member = await asMember.GetAsync(Path("service-orders/ord-1-child/parent"));

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
        (await member.ReadJsonAsync()).GetProperty("id").GetString().Should().Be("ord-1");
    }

    // ── Lists ───────────────────────────────────────────────────────────────────

    public static TheoryData<string> ListRoutes() =>
    [
        "service-orders",
        "service-orders/roots",
        "service-orders/ord-1/children",
        "service-orders/by-status/Draft",
        "service-orders/by-assignee/tech-a",
        "service-orders/by-creator/creator-a",
        "service-orders/by-order-type/pole-survey",
        "service-orders/by-date-range?from=2026-01-01T00:00:00Z&to=2027-01-01T00:00:00Z",
        "service-orders/dispatched-to/crew-9?targetType=Group",
    ];

    [Theory]
    [MemberData(nameof(ListRoutes))]
    public async Task XD01_332_ListRoutes_NeverReturnAnOrderOfAnotherOrganization_EvenWhenTheStoreWould(string route)
    {
        // The store behind this host applies no tenant filter at all, so every list hands the
        // route orders of both organizations — the worst case the route must still narrow.
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        foreach (var id in new[] { "ord-1", "ord-1-child", "ord-b" })
            await host.Orders.AppendDispatchAsync(id, new OrderDispatch("crew-9", DispatchTargetType.Group, "d", OrderTargetsApiHost.Now.UtcDateTime));
        using var asAdminOfB = host.ClientFor("admin-b");
        using var asAdminOfA = host.ClientFor("admin");
        using var asMemberOfA = host.ClientFor("reader");

        var seenByB = await IdsAsync(await asAdminOfB.GetAsync(Path(route)));
        var seenByA = await IdsAsync(await asAdminOfA.GetAsync(Path(route)));
        var seenByMemberOfA = await IdsAsync(await asMemberOfA.GetAsync(Path(route)));

        seenByB.Should().NotContain(["ord-1", "ord-1-child"]);
        seenByA.Should().NotContain("ord-b").And.NotBeEmpty();
        seenByMemberOfA.Should().Equal(seenByA, "a member without a role sees its organization's list, as before");
    }

    [Fact]
    public async Task XD01_332_ListRoutes_GranteeAndDispatchRecipient_SeeTheOrdersTheGateLetsThemReach()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("outsider");

        var before = await IdsAsync(await client.GetAsync(Path("service-orders")));
        await host.Orders.AppendDispatchAsync(
            "ord-1-child", new OrderDispatch(OrgB.ToString(), DispatchTargetType.Organization, "d", OrderTargetsApiHost.Now.UtcDateTime));
        var asRecipient = await IdsAsync(await client.GetAsync(Path("service-orders")));
        GrantOrgB(host, "serviceorders:view");
        var asGrantee = await IdsAsync(await client.GetAsync(Path("service-orders")));

        before.Should().BeEquivalentTo(["ord-b"]);
        asRecipient.Should().BeEquivalentTo(["ord-b", "ord-1-child"]);
        asGrantee.Should().BeEquivalentTo(["ord-b", "ord-1-child", "ord-1"]);
    }

    // ── Writes on an existing order ─────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SingleOrderWrites))]
    public async Task XD01_332_Writes_AdministratorOfAnotherOrganization_GetTheSame404AsForAnUnknownOrder_AndChangeNothing(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin-b");

        var foreign = await WriteAsync(client, route, "ord-1");
        var unknown = await WriteAsync(client, route, "ord-0");

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync()).And.BeEmpty();
        var stored = await StoredAsync(host, "ord-1");
        stored.Title.Should().Be("title");
        stored.Dispatches.Should().BeEmpty();
        stored.ActionLog.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(SingleOrderWrites))]
    public async Task XD01_332_Writes_AdministratorOfTheOrdersOwnOrganization_AreServedAsBefore(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        (await WriteAsync(client, route, "ord-1")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [MemberData(nameof(SingleOrderWrites))]
    public async Task XD01_332_Writes_MemberOfTheOrdersOrganizationWithoutARole_KeepTheExisting403WithAReason(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        using var client = host.ClientFor("reader");

        var response = await WriteAsync(client, route, "ord-1");

        var body = await response.ReadJsonAsync(HttpStatusCode.Forbidden);
        body.GetProperty("reason").GetString().Should().Be("No rule granted this action.");
    }

    [Fact]
    public async Task XD01_332_Writes_Grantee_MayDoWhatItsGrantLists_AndIsForbiddenNotHiddenForTheRest()
    {
        await using var host = await HostAsync();
        GrantOrgB(host, "serviceorders:view", "serviceorders:annotate");
        using var client = host.ClientFor("outsider");

        var read = await client.GetAsync(Path("service-orders/ord-1"));
        var annotate = await WriteAsync(client, "POST actions", "ord-1");
        var dispatch = await WriteAsync(client, "POST dispatch", "ord-1");

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        annotate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await dispatch.ReadJsonAsync(HttpStatusCode.Forbidden)).GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(DispatchTargetType.Organization)]
    [InlineData(DispatchTargetType.User)]
    public async Task XD01_332_DispatchRecipientOfAnotherOrganization_ReadsAndAnnotates_AndIsForbiddenNotHiddenForTheRest(DispatchTargetType targetType)
    {
        await using var host = await HostAsync();
        var recipientId = targetType == DispatchTargetType.Organization
            ? OrgB.ToString()
            : OrderTargetsApiHost.Identities["outsider"].UserId.ToString();
        await host.Orders.AppendDispatchAsync("ord-1", new OrderDispatch(recipientId, targetType, "d", OrderTargetsApiHost.Now.UtcDateTime));
        using var client = host.ClientFor("outsider");

        var read = await client.GetAsync(Path("service-orders/ord-1"));
        var annotate = await WriteAsync(client, "POST actions", "ord-1");
        var delete = await WriteAsync(client, "DELETE", "ord-1");

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        annotate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── OrganizationId is fixed at creation ─────────────────────────────────────

    [Fact]
    public async Task XD01_332_Put_GranteeRelabellingAGrantedOrderToItsOwnOrganization_Returns400_AndTheOrderStaysWhereItWas()
    {
        await using var host = await HostAsync();
        GrantOrgB(host, "serviceorders:view", "serviceorders:annotate");
        using var client = host.ClientFor("outsider");

        var response = await client.PutAsJsonAsync(Path("service-orders/ord-1"), Order("ord-1", OrgB, title: "mine now"));

        var body = await response.ReadJsonAsync(HttpStatusCode.BadRequest);
        body.GetProperty("reason").GetString().Should().Contain("organization");
        body.GetProperty("code").GetString().Should().Be(ServiceOrderOrganization.ChangeRefusedCode);
        var stored = await StoredAsync(host, "ord-1");
        stored.OrganizationId.Should().Be(OrgA);
        stored.Title.Should().Be("title");
    }

    [Fact]
    public async Task XD01_332_Put_AdministratorOfTheOwningOrganization_CannotMoveTheOrderEither()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var response = await client.PutAsJsonAsync(Path("service-orders/ord-1"), Order("ord-1", OrgB));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StoredAsync(host, "ord-1")).OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public async Task XD01_332_Put_BodyThatOmitsTheOrganization_UpdatesTheOrder_AndKeepsItsOrganization()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var response = await client.PutAsync(
            Path("service-orders/ord-1"),
            JsonContent.Create(new { id = "ord-1", title = "edited", orderTypeId = OrderTargetsApiHost.PoleSurvey, status = ServiceOrderStatus.Draft }));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var stored = await StoredAsync(host, "ord-1");
        stored.OrganizationId.Should().Be(OrgA);
        stored.Title.Should().Be("edited");
    }

    // ── Creation ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task XD01_332_Create_BodyThatOmitsTheOrganization_IsStoredUnderTheCallersOwn()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin-b");

        var response = await client.PostAsync(
            Path("service-orders"),
            JsonContent.Create(new { id = "ord-new", title = "new", orderTypeId = OrderTargetsApiHost.PoleSurvey, createdBy = "x" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await StoredAsync(host, "ord-new")).OrganizationId.Should().Be(OrgB);
    }

    [Fact]
    public async Task XD01_332_Create_ForTheCallersOwnOrganization_IsStoredAsSent()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin-b");

        var response = await client.PostAsJsonAsync(Path("service-orders"), Order("ord-new", OrgB));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await StoredAsync(host, "ord-new")).OrganizationId.Should().Be(OrgB);
    }

    [Fact]
    public async Task XD01_332_Create_ForAnotherOrganization_Returns403_AndStoresNothing_EvenForAGrantee()
    {
        await using var host = await HostAsync();
        GrantOrgB(host, "serviceorders:view", "serviceorders:annotate", "serviceorders:create");
        using var client = host.ClientFor("admin-b");

        var response = await client.PostAsJsonAsync(Path("service-orders"), Order("ord-new", OrgA));

        (await response.ReadJsonAsync(HttpStatusCode.Forbidden)).GetProperty("reason").GetString().Should().Contain("own organization");
        (await host.Orders.GetByIdAsync("ord-new")).Should().BeNull();
    }

    // ── Client and server agree ─────────────────────────────────────────────────

    [Fact]
    public void XD01_332_OwnOrganizationGrants_RoundTripTheWireShape_IntoThePrincipalTheServerBuilds()
    {
        // What GET /api/identity/organization-grants/me sends is OwnOrganizationGrantDto.FromModel
        // of the same repository rows ServerWorkflowPrincipalFactory reads; a client rebuilds the
        // grant with ToModel. Serialized and back, nothing the gate reads is lost.
        var grant = new GeoAssets.Identity.Authorization.Models.OrganizationGrant
        {
            GranteeOrganizationId = OrgB,
            ResourceOrganizationId = OrgA,
            ResourceType = "ServiceOrder",
            ScopeId = "municipio-123",
            AllowedActions = { "serviceorders:view" },
            RequiredRole = "Supervisor",
            ExpiresAt = OrderTargetsApiHost.Now.UtcDateTime.AddDays(1),
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var wire = JsonSerializer.Serialize(GeoAssets.Identity.Authorization.Services.OwnOrganizationGrantDto.FromModel(grant), options);
        var rebuilt = JsonSerializer.Deserialize<GeoAssets.Identity.Authorization.Services.OwnOrganizationGrantDto>(wire, options)!.ToModel(OrgB);

        rebuilt.Should().BeEquivalentTo(grant, o => o.Excluding(g => g.Id).Excluding(g => g.GrantedBy).Excluding(g => g.GrantedAt));
        wire.Should().NotContain("grantedBy").And.NotContain("granteeOrganizationId");
    }
}
