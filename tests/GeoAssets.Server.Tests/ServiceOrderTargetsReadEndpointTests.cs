using System.Net;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The read routes of <c>MapServiceOrderTargetsApi()</c> (XD01-308): the paged target list
/// (R1), count (R2), extent (R3), one target, and the usages lookup (R4) — happy paths, every
/// status code they map, authentication, authorization and paging bounds.
/// </summary>
public sealed class ServiceOrderTargetsReadEndpointTests
{
    // ── R1: GET /service-orders/{id}/targets ──────────────────────────────────

    [Fact]
    public async Task GetTargets_ReturnsThePagedEnvelope_WithTheRequestedPageAndTheTotalCount()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedTargetsAsync("ord-1", 5);
        using var client = host.ClientFor("reader");

        var body = await (await client.GetAsync(Path("service-orders/ord-1/targets?page=2&pageSize=2"))).ReadJsonAsync();

        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("pageSize").GetInt32().Should().Be(2);
        body.GetProperty("totalCount").GetInt32().Should().Be(5);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("featureId").GetString()).Should().Equal("f-003", "f-004");
        items[0].GetProperty("orderId").GetString().Should().Be("ord-1");
        items[0].GetProperty("alias").GetString().Should().Be("P-003");
        items[0].GetProperty("version").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetTargets_WithoutPagingValues_UsesPageOneAndFiftyPerPage()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedTargetsAsync("ord-1", 60);
        using var client = host.ClientFor("reader");

        var body = await (await client.GetAsync(Path("service-orders/ord-1/targets"))).ReadJsonAsync();

        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("pageSize").GetInt32().Should().Be(50);
        body.GetProperty("items").GetArrayLength().Should().Be(50);
        body.GetProperty("totalCount").GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task GetTargets_SortAndFilters_AreApplied_AndAnUnknownSortKeyFallsBackInsteadOfFailing()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        var seeded = await host.SeedTargetsAsync("ord-1", 3);
        await host.Store.UpdateAsync(
            seeded[1] with { Status = OrderTargetStatus.InProgress, ConflictLevel = OrderTargetConflictLevel.Warning }, CancellationToken.None);
        using var client = host.ClientFor("reader");

        var byAliasDescending = await (await client.GetAsync(Path("service-orders/ord-1/targets?sortBy=alias&descending=true"))).ReadJsonAsync();
        var unknownSort = await (await client.GetAsync(Path("service-orders/ord-1/targets?sortBy=nonsense"))).ReadJsonAsync();
        var inProgress = await (await client.GetAsync(Path("service-orders/ord-1/targets?status=InProgress"))).ReadJsonAsync();
        var warningByName = await (await client.GetAsync(Path("service-orders/ord-1/targets?conflictLevel=warning"))).ReadJsonAsync();
        var warningByNumber = await (await client.GetAsync(Path("service-orders/ord-1/targets?conflictLevel=1"))).ReadJsonAsync();

        byAliasDescending.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("alias").GetString())
            .Should().Equal("P-003", "P-002", "P-001");
        unknownSort.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("featureId").GetString())
            .Should().Equal("f-001", "f-002", "f-003");
        inProgress.GetProperty("totalCount").GetInt32().Should().Be(1);
        warningByName.GetProperty("totalCount").GetInt32().Should().Be(1);
        warningByNumber.GetProperty("items")[0].GetProperty("featureId").GetString().Should().Be("f-002");
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    [InlineData("page=abc")]
    [InlineData("pageSize=1.5")]
    [InlineData("page=2147483647&pageSize=200")]
    public async Task GetTargets_PagingOutOfRange_Returns400PagingOutOfRange(string query)
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        using var client = host.ClientFor("reader");

        var response = await client.GetAsync(Path($"service-orders/ord-1/targets?{query}"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange);
    }

    [Fact]
    public async Task GetTargets_PageSizeExactlyAtTheMaximum_IsAccepted()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        using var client = host.ClientFor("reader");

        var body = await (await client.GetAsync(Path("service-orders/ord-1/targets?pageSize=200&page=3"))).ReadJsonAsync();

        body.GetProperty("items").GetArrayLength().Should().Be(0);
        body.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData("status=Nope")]
    [InlineData("status=pending")]
    [InlineData("conflictLevel=Severe")]
    [InlineData("conflictLevel=7")]
    [InlineData("descending=maybe")]
    public async Task GetTargets_InvalidFilterValue_Returns400ValidationFailed(string query)
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        using var client = host.ClientFor("reader");

        var response = await client.GetAsync(Path($"service-orders/ord-1/targets?{query}"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
    }

    // ── R2 / R3 / one target ──────────────────────────────────────────────────

    [Fact]
    public async Task Count_ReturnsTheNumberOfTargets()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedTargetsAsync("ord-1", 7);
        using var client = host.ClientFor("reader");

        var body = await (await client.GetAsync(Path("service-orders/ord-1/targets/count"))).ReadJsonAsync();

        body.GetProperty("count").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task Extent_WithPositionedTargets_Returns200WithMinLonMinLatMaxLonMaxLat()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedTargetsAsync("ord-1", 3);
        using var client = host.ClientFor("reader");

        var body = await (await client.GetAsync(Path("service-orders/ord-1/targets/extent"))).ReadJsonAsync();

        body.GetProperty("bbox").EnumerateArray().Select(n => n.GetDouble()).Should().Equal(-41, -15, -39, -13);
    }

    [Fact]
    public async Task Extent_OrderWithoutTargets_Returns204()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        using var client = host.ClientFor("reader");

        var response = await client.GetAsync(Path("service-orders/ord-1/targets/extent"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetTarget_ReturnsTheTarget_And404ForAnUnknownIdOrAnotherOrdersTarget()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedOrderAsync("ord-2");
        await host.SeedTargetsAsync("ord-1", 2);
        await host.SeedTargetsAsync("ord-2", 1);
        using var client = host.ClientFor("reader");

        var found = await (await client.GetAsync(Path("service-orders/ord-1/targets/ord-1-t002"))).ReadJsonAsync();
        var missing = await client.GetAsync(Path("service-orders/ord-1/targets/nope"));
        var ofAnotherOrder = await client.GetAsync(Path("service-orders/ord-1/targets/ord-2-t001"));

        found.GetProperty("featureId").GetString().Should().Be("f-002");
        var problem = await missing.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound);
        problem.GetProperty("targetId").GetString().Should().Be("nope");
        await ofAnotherOrder.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound);
    }

    // ── Shared by R1–R3 and the single-target route ───────────────────────────

    public static TheoryData<string> OrderReadRoutes() => new()
    {
        "service-orders/{0}/targets",
        "service-orders/{0}/targets/count",
        "service-orders/{0}/targets/extent",
        "service-orders/{0}/targets/some-target",
    };

    [Theory]
    [MemberData(nameof(OrderReadRoutes))]
    public async Task ReadRoutes_UnknownOrder_Return404OrdersNotFound(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await OrderTargetsApiHost.StartAsync();
        using var client = host.ClientFor("reader");

        var response = await client.GetAsync(Path(string.Format(System.Globalization.CultureInfo.InvariantCulture, route, "ord-missing")));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        problem.GetProperty("orderId").GetString().Should().Be("ord-missing");
    }

    [Theory]
    [MemberData(nameof(OrderReadRoutes))]
    public async Task ReadRoutes_WithoutAToken_Return401(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        using var client = host.ClientFor(token: null);

        var response = await client.GetAsync(Path(string.Format(System.Globalization.CultureInfo.InvariantCulture, route, "ord-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(OrderReadRoutes))]
    public async Task ReadRoutes_WithoutTheServiceOrdersReadPermission_Return403(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        using var client = host.ClientFor("no-permission"); // an Administrator, but without the permission

        var response = await client.GetAsync(Path(string.Format(System.Globalization.CultureInfo.InvariantCulture, route, "ord-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(OrderReadRoutes))]
    public async Task ReadRoutes_CallerOfAnotherOrganizationWithNoGrant_Return404OrdersNotFound_EvenWithThePermission(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedTargetsAsync("ord-1", 2);
        using var client = host.ClientFor("outsider");

        var response = await client.GetAsync(Path(string.Format(System.Globalization.CultureInfo.InvariantCulture, route, "ord-1")));

        // XD01-332: refused as before, but with the response an unknown order gets (was 403
        // auth.denied), so the refusal does not confirm the order exists.
        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        problem.GetProperty("orderId").GetString().Should().Be("ord-1");
        problem.TryGetProperty("reason", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ReadRoutes_CallerOfAnotherOrganizationHoldingAViewGrant_IsAllowed()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedTargetsAsync("ord-1", 2);
        host.Grants.Active.Add(new()
        {
            GranteeOrganizationId = OrderTargetsApiHost.OrgB,
            ResourceOrganizationId = OrderTargetsApiHost.OrgA,
            AllowedActions = { "serviceorders:view" },
        });
        using var client = host.ClientFor("outsider");

        var body = await (await client.GetAsync(Path("service-orders/ord-1/targets/count"))).ReadJsonAsync();

        body.GetProperty("count").GetInt32().Should().Be(2);
    }

    // ── R4: POST /service-orders/targets/usages ───────────────────────────────

    [Fact]
    public async Task Usages_ReturnsTheOpenUsagesOfTheListedFeatures_PagedAndSortedByFeatureThenOrder()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedOrderAsync("ord-b", OrderTargetsApiHost.BlockingSurvey, ServiceOrderStatus.InProgress);
        await host.SeedOrderAsync("ord-a", status: ServiceOrderStatus.Pending);
        await host.SeedOrderAsync("ord-closed", status: ServiceOrderStatus.Completed);
        await host.SeedTargetsAsync("ord-b", 2);
        await host.SeedTargetsAsync("ord-a", 1);
        await host.SeedTargetsAsync("ord-closed", 2);
        using var client = host.ClientFor("reader");

        var all = await (await client.PostJsonAsync(
            "service-orders/targets/usages", new { featureIds = new[] { "f-002", "f-001", "f-404" }, excludingOrderId = "ord-1" })).ReadJsonAsync();
        var secondPage = await (await client.PostJsonAsync(
            "service-orders/targets/usages",
            new { featureIds = new[] { "f-001", "f-002" }, excludingOrderId = "ord-1", page = 2, pageSize = 2 })).ReadJsonAsync();

        all.GetProperty("totalCount").GetInt32().Should().Be(3);
        all.GetProperty("items").EnumerateArray()
            .Select(u => (u.GetProperty("featureId").GetString(), u.GetProperty("orderId").GetString()))
            .Should().Equal(("f-001", "ord-a"), ("f-001", "ord-b"), ("f-002", "ord-b"));
        var blocking = all.GetProperty("items")[1];
        blocking.GetProperty("orderTypeId").GetString().Should().Be(OrderTargetsApiHost.BlockingSurvey);
        blocking.GetProperty("orderStatus").GetString().Should().Be(ServiceOrderStatus.InProgress);
        blocking.GetProperty("exclusivity").GetInt32().Should().Be((int)OrderTargetExclusivity.BlockOnOverlap);
        secondPage.GetProperty("items").GetArrayLength().Should().Be(1);
        secondPage.GetProperty("page").GetInt32().Should().Be(2);
        secondPage.GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Usages_ZeroIdsOrMoreThanFiveHundredOrABlankId_Return400ValidationFailed()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        using var client = host.ClientFor("reader");
        var tooMany = Enumerable.Range(1, 501).Select(i => $"f-{i}").ToArray();

        var none = await client.PostJsonAsync("service-orders/targets/usages", new { featureIds = Array.Empty<string>(), excludingOrderId = "ord-1" });
        var missing = await client.PostJsonAsync("service-orders/targets/usages", new { excludingOrderId = "ord-1" });
        var over = await client.PostJsonAsync("service-orders/targets/usages", new { featureIds = tooMany, excludingOrderId = "ord-1" });
        var blank = await client.PostJsonAsync("service-orders/targets/usages", new { featureIds = new[] { "f-1", " " }, excludingOrderId = "ord-1" });
        var noOrder = await client.PostJsonAsync("service-orders/targets/usages", new { featureIds = new[] { "f-1" } });
        var atTheLimit = await client.PostJsonAsync(
            "service-orders/targets/usages", new { featureIds = tooMany.Take(500).ToArray(), excludingOrderId = "ord-1" });

        foreach (var response in new[] { none, missing, over, blank, noOrder })
            await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
        atTheLimit.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public async Task Usages_PagingOutOfRange_Returns400PagingOutOfRange(int page, int pageSize)
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        using var client = host.ClientFor("reader");

        var response = await client.PostJsonAsync(
            "service-orders/targets/usages", new { featureIds = new[] { "f-1" }, excludingOrderId = "ord-1", page, pageSize });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange);
    }

    [Fact]
    public async Task Usages_MalformedBody_Returns400ValidationFailed()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        using var client = host.ClientFor("reader");

        var malformed = await client.PostRawAsync("service-orders/targets/usages", "{ not json");
        var notAnObject = await client.PostRawAsync("service-orders/targets/usages", "[1,2]");
        var empty = await client.PostRawAsync("service-orders/targets/usages", "");

        foreach (var response in new[] { malformed, notAnObject, empty })
            await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
    }

    [Fact]
    public async Task Usages_ExcludedOrderOfAnotherOrganization_IsTreatedAsUnknown_SoItsOrganizationsUsagesAreNotDisclosed()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedOrderAsync("ord-other", status: ServiceOrderStatus.InProgress);
        await host.SeedTargetsAsync("ord-other", 1);
        using var client = host.ClientFor("outsider");

        var response = await client.PostJsonAsync(
            "service-orders/targets/usages", new { featureIds = new[] { "f-001" }, excludingOrderId = "ord-1" });

        // XD01-332: was 403 auth.denied, which confirmed "ord-1" exists in another organization.
        // Now answered exactly like an excluded order that does not exist: the caller's own
        // organization is searched, so organization A's usage of f-001 stays undisclosed.
        var body = await response.ReadJsonAsync();
        body.GetProperty("items").GetArrayLength().Should().Be(0);
        body.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Usages_AuthenticationAndPermission_AreRequired()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        var body = new { featureIds = new[] { "f-001" }, excludingOrderId = "ord-1" };
        using var anonymous = host.ClientFor(token: null);
        using var unpermitted = host.ClientFor("no-permission");

        (await anonymous.PostJsonAsync("service-orders/targets/usages", body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await unpermitted.PostJsonAsync("service-orders/targets/usages", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
