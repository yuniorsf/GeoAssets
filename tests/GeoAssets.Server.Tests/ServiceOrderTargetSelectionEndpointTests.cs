using System.Net;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The selection routes of <c>MapServiceOrderTargetsApi()</c> (XD01-308): preview (R5), apply
/// (R6) and <c>POST /selection/resolve</c> — against the real selection service and strategies,
/// with in-memory stores behind them.
/// </summary>
public sealed class ServiceOrderTargetSelectionEndpointTests
{
    private const string Preview = "service-orders/ord-1/targets/selection/preview";
    private const string Apply = "service-orders/ord-1/targets/selection/apply";
    private const string Resolve = "selection/resolve";

    private static async Task<OrderTargetsApiHost> HostAsync(
        string orderTypeId = OrderTargetsApiHost.PoleSurvey,
        string status = ServiceOrderStatus.Draft,
        Action<OrderTargetSelectionOptions>? configure = null)
    {
        var host = await OrderTargetsApiHost.StartAsync(configure);
        host.SeedAssets();
        await host.SeedOrderAsync(orderTypeId: orderTypeId, status: status);
        return host;
    }

    // ── R5: preview ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_PolygonFence_Returns200WithTheCountsAndTheFirstPage_AndWritesNothing()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var body = await (await client.PostJsonAsync(Preview, FenceBody(pageSize: 2))).ReadJsonAsync();

        body.GetProperty("matched").GetInt32().Should().Be(3, "three poles of organization A are inside; the manhole and B's poles are not eligible");
        body.GetProperty("eligible").GetInt32().Should().Be(3);
        body.GetProperty("alreadyInOrder").GetInt32().Should().Be(0);
        body.GetProperty("truncatedByLimit").GetInt32().Should().Be(0);
        body.GetProperty("orderTypeId").GetString().Should().Be(OrderTargetsApiHost.PoleSurvey);
        var page = body.GetProperty("firstPage");
        page.GetProperty("totalCount").GetInt32().Should().Be(3);
        page.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("featureId").GetString()).Should().Equal("a-pole-1", "a-pole-2");
        page.GetProperty("items")[0].GetProperty("alias").GetString().Should().Be("P-100");
        host.Store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_IdentifierList_DefaultsToTheAliasAttribute_AndReportsUnknownAndAmbiguousValues()
    {
        await using var host = await HostAsync();
        host.Assets.Add(OrderTargetsApiHost.Asset("a-pole-dup", 5, 5, OrderTargetsApiHost.OrgA, plate: "P-200"));
        using var client = host.ClientFor("admin");

        var body = await (await client.PostJsonAsync(
            Preview, new { strategyId = "identifier-list", parameters = new { values = new[] { "P-100", "P-200", "P-404" } } })).ReadJsonAsync();

        body.GetProperty("matched").GetInt32().Should().Be(3);
        body.GetProperty("notFoundValues").Strings().Should().Equal("P-404");
        body.GetProperty("ambiguousValues").Strings().Should().Equal("P-200");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ broken")]
    [InlineData("null")]
    [InlineData("""{ "parameters": {} }""")]
    [InlineData("""{ "strategyId": "  ", "parameters": {} }""")]
    [InlineData("""{ "strategyId": 5 }""")]
    public async Task PreviewAndApply_MissingOrMalformedBody_Return400ValidationFailed(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        await (await client.PostRawAsync(Preview, body)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
        await (await client.PostRawAsync(Apply, body)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
    }

    [Fact]
    public async Task PreviewAndApply_UnknownStrategy_Return400UnknownStrategy_Not404()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        var body = new { strategyId = "no-such-strategy", parameters = new { } };

        var preview = await (await client.PostJsonAsync(Preview, body))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.UnknownSelectionStrategy);
        await (await client.PostJsonAsync(Apply, body))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.UnknownSelectionStrategy);

        preview.GetProperty("strategyId").GetString().Should().Be("no-such-strategy");
    }

    [Fact]
    public async Task PreviewAndApply_UnknownOrder_Return404OrdersNotFound_EvenWithAnUnknownStrategy()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        var body = new { strategyId = "no-such-strategy", parameters = new { } };

        await (await client.PostJsonAsync("service-orders/ord-missing/targets/selection/preview", body))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        await (await client.PostJsonAsync("service-orders/ord-missing/targets/selection/apply", body))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
    }

    [Fact]
    public async Task PreviewAndApply_InvalidFence_Return400InvalidParameters_NamingTheDefect()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        double[][][] unclosed = [[[0, 0], [10, 0], [10, 10], [0, 10]]];
        double[][][] bowTie = [[[0, 0], [10, 10], [10, 0], [0, 10], [0, 0]]];

        var notClosed = await (await client.PostJsonAsync(Preview, FenceBody(unclosed)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters);
        var selfIntersecting = await (await client.PostJsonAsync(Apply, FenceBody(bowTie)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters);

        notClosed.GetProperty("defect").GetString().Should().Be("NotClosed");
        selfIntersecting.GetProperty("defect").GetString().Should().Be("SelfIntersecting");
        host.Store.Snapshot().Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "strategyId": "polygon-fence", "parameters": {} }""")]                                     // rings missing
    [InlineData("""{ "strategyId": "polygon-fence" }""")]                                                       // no parameters at all
    [InlineData("""{ "strategyId": "polygon-fence", "parameters": { "rings": "a square" } }""")]                // wrong type
    [InlineData("""{ "strategyId": "polygon-fence", "parameters": { "rings": [[[0,0],[1,0],[1,1],[0,0]]], "maxResults": 0 } }""")]
    [InlineData("""{ "strategyId": "polygon-fence", "parameters": { "rings": [[[0,0],[1,0],[1,1],[0,0]]], "maxResults": "many" } }""")]
    [InlineData("""{ "strategyId": "polygon-fence", "parameters": { "rings": [[[0,0],[1,0],[1,1],[0,0]]], "assetTypeIds": [1, 2] } }""")]
    [InlineData("""{ "strategyId": "identifier-list", "parameters": { "attributeKey": "plate" } }""")]          // values missing
    [InlineData("""{ "strategyId": "identifier-list", "parameters": { "values": ["  ", ""] } }""")]             // nothing left
    [InlineData("""{ "strategyId": "identifier-list", "parameters": { "values": "P-1" } }""")]                  // not a list
    public async Task PreviewAndApply_BadOrMissingParameters_Return400InvalidParameters(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        await (await client.PostRawAsync(Preview, body))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters);
        await (await client.PostRawAsync(Apply, body))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters);
        host.Store.Snapshot().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    [InlineData(2147483647, 200)]
    public async Task PreviewAndApply_PagingOutOfRange_Return400PagingOutOfRange(int page, int pageSize)
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        await (await client.PostJsonAsync(Preview, FenceBody(page: page, pageSize: pageSize)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange);
        await (await client.PostJsonAsync(Apply, FenceBody(page: page, pageSize: pageSize)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange);
    }

    [Fact]
    public async Task Preview_CallerWithoutViewOnTheOrder_Returns403AuthDenied_WithTheRuleEnginesReason()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("reader");

        var problem = await (await client.PostJsonAsync(Preview, FenceBody()))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);

        problem.GetProperty("reason").GetString().Should().Be("No rule granted this action.");
    }

    [Fact]
    public async Task Preview_AuthenticationAndTheReadPermission_AreRequired()
    {
        await using var host = await HostAsync();
        using var anonymous = host.ClientFor(token: null);
        using var unpermitted = host.ClientFor("no-permission");

        (await anonymous.PostJsonAsync(Preview, FenceBody())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await unpermitted.PostJsonAsync(Preview, FenceBody())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PreviewAndApply_OrderTypeWithoutATargetPolicy_Return422NoTargetPolicy()
    {
        await using var host = await HostAsync(orderTypeId: "inspection");
        using var client = host.ClientFor("admin");

        var problem = await (await client.PostJsonAsync(Preview, FenceBody()))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.NoTargetPolicy);
        await (await client.PostJsonAsync(Apply, FenceBody()))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.NoTargetPolicy);

        problem.GetProperty("orderTypeId").GetString().Should().Be("inspection");
    }

    [Fact]
    public async Task PreviewAndApply_OrderWhoseTypeIsNotRegistered_Return404OrderTypeNotFound()
    {
        await using var host = await HostAsync(orderTypeId: "ghost-type");
        using var client = host.ClientFor("admin");

        await (await client.PostJsonAsync(Preview, FenceBody()))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderTypeNotFound);
        await (await client.PostJsonAsync(Apply, FenceBody()))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderTypeNotFound);
    }

    [Fact]
    public async Task PreviewAndApply_MoreMatchesThanTheHostCeiling_Return422SelectionTooLarge()
    {
        await using var host = await HostAsync(configure: o => o.MaxMatched = 2);
        using var client = host.ClientFor("admin");

        var problem = await (await client.PostJsonAsync(Preview, FenceBody()))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.SelectionTooLarge);
        await (await client.PostJsonAsync(Apply, FenceBody(acceptTruncation: true)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.SelectionTooLarge);

        problem.GetProperty("ceiling").GetInt32().Should().Be(2);
        problem.GetProperty("matchedAtLeast").GetInt32().Should().Be(3);
        host.Store.Snapshot().Should().BeEmpty();
    }

    // ── R6: apply ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_Returns200WithTheResult_StoresTheTargets_StampsTheSpec_AndLogsOneAnnotateEntry()
    {
        await using var host = await HostAsync();
        var admin = OrderTargetsApiHost.Identities["admin"];
        using var client = host.ClientFor("admin");

        var body = await (await client.PostJsonAsync(Apply, FenceBody())).ReadJsonAsync();

        body.GetProperty("added").GetInt32().Should().Be(3);
        body.GetProperty("matched").GetInt32().Should().Be(3);
        var stored = host.Store.Snapshot();
        stored.Select(t => t.FeatureId).Should().BeEquivalentTo("a-pole-1", "a-pole-2", "a-pole-3");
        stored.Select(t => (t.AddedBy, t.AddedAt, t.OrganizationId)).Distinct()
            .Should().Equal((admin.UserId.ToString(), OrderTargetsApiHost.Now.UtcDateTime, OrderTargetsApiHost.OrgA));
        var order = (ServiceOrder)(await host.Orders.GetByIdAsync("ord-1"))!;
        order.SelectionSpec!.StrategyId.Should().Be("polygon-fence");
        order.ActionLog.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Action = OrderActionType.Annotate, PerformedBy = admin.UserId.ToString(), PerformedAt = OrderTargetsApiHost.Now.UtcDateTime });
    }

    [Fact]
    public async Task Apply_Twice_TheSecondCallAddsNothing()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        await (await client.PostJsonAsync(Apply, FenceBody())).ReadJsonAsync();
        var second = await (await client.PostJsonAsync(Apply, FenceBody())).ReadJsonAsync();

        second.GetProperty("added").GetInt32().Should().Be(0);
        second.GetProperty("alreadyInOrder").GetInt32().Should().Be(3);
        host.Store.Snapshot().Should().HaveCount(3);
    }

    [Fact]
    public async Task Apply_OverTheSelectionLimit_Returns422LimitExceeded_WithLimitCurrentAndRequested_AndWritesNothing()
    {
        await using var host = await HostAsync(OrderTargetsApiHost.LimitedSurvey);
        using var client = host.ClientFor("admin");

        var preview = await (await client.PostJsonAsync(Preview, FenceBody())).ReadJsonAsync();
        var problem = await (await client.PostJsonAsync(Apply, FenceBody()))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.TargetLimitExceeded);

        preview.GetProperty("truncatedByLimit").GetInt32().Should().Be(1, "the preview flags it instead of failing");
        problem.GetProperty("limit").GetInt32().Should().Be(2);
        problem.GetProperty("current").GetInt32().Should().Be(0);
        problem.GetProperty("requested").GetInt32().Should().Be(3);
        host.Store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_OverTheSelectionLimitWithAcceptTruncation_Returns200_AndAddsOnlyWhatFits()
    {
        await using var host = await HostAsync(OrderTargetsApiHost.LimitedSurvey);
        using var client = host.ClientFor("admin");

        var body = await (await client.PostJsonAsync(Apply, FenceBody(acceptTruncation: true))).ReadJsonAsync();

        body.GetProperty("added").GetInt32().Should().Be(2);
        body.GetProperty("truncatedByLimit").GetInt32().Should().Be(1);
        host.Store.Snapshot().Select(t => t.FeatureId).Should().BeEquivalentTo("a-pole-1", "a-pole-2");
    }

    [Fact]
    public async Task Apply_OrderInATerminalState_Returns409InvalidState()
    {
        await using var host = await HostAsync(status: ServiceOrderStatus.Completed);
        using var client = host.ClientFor("admin");

        var problem = await (await client.PostJsonAsync(Apply, FenceBody()))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, WorkflowProblemCodes.OrderInvalidState);

        problem.GetProperty("from").GetString().Should().Be(ServiceOrderStatus.Completed);
        host.Store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_OrderChangedBySomeoneElseWhileApplying_Returns409ConcurrentModification()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(decorateOrders: inner => new StaleOrderRepository(inner));
        host.SeedAssets();
        await host.SeedOrderAsync();
        using var client = host.ClientFor("admin");

        var problem = await (await client.PostJsonAsync(Apply, FenceBody()))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, WorkflowProblemCodes.TargetConcurrentModification);

        problem.GetProperty("orderId").GetString().Should().Be("ord-1");
    }

    [Theory]
    [InlineData("reader")]
    // XD01-332: a caller of another organization no longer gets this 403 — it gets the same 404
    // as an unknown order; see ServiceOrderTargetsOrganizationGateTests.
    public async Task Apply_CallerTheOrderRulesRefuse_Returns403AuthDenied_AndWritesNothing(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        await using var host = await HostAsync();
        using var client = host.ClientFor(token);

        await (await client.PostJsonAsync(Apply, FenceBody())).ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);

        host.Store.Snapshot().Should().BeEmpty();
        ((ServiceOrder)(await host.Orders.GetByIdAsync("ord-1"))!).ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_ThePrincipalComesFromTheToken_NeverFromTheUrlOrTheBody()
    {
        await using var host = await HostAsync();
        var admin = OrderTargetsApiHost.Identities["admin"];
        var reader = OrderTargetsApiHost.Identities["reader"];
        using var asReader = host.ClientFor("reader");
        using var asAdmin = host.ClientFor("admin");

        // A reader claiming, in every place a client controls, to be the administrator.
        var forgedByReader = await asReader.PostRawAsync(
            $"{Apply}?userId={admin.UserId}&organizationId={OrderTargetsApiHost.OrgA}",
            $$"""
              {
                "strategyId": "polygon-fence",
                "parameters": { "rings": [[[0,0],[10,0],[10,10],[0,10],[0,0]]] },
                "userId": "{{admin.UserId}}",
                "principal": { "userId": "{{admin.UserId}}", "roleNames": ["Administrator"], "permissionCodes": ["serviceorders:annotate"] }
              }
              """);
        await forgedByReader.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        host.Store.Snapshot().Should().BeEmpty();

        // An administrator claiming to be someone else: the targets are still attributed to the token's user.
        var byAdmin = await asAdmin.PostRawAsync(
            $"{Apply}?userId={reader.UserId}",
            $$"""
              {
                "strategyId": "polygon-fence",
                "parameters": { "rings": [[[0,0],[10,0],[10,10],[0,10],[0,0]]], "organizationId": "{{OrderTargetsApiHost.OrgB}}" },
                "principal": { "userId": "{{reader.UserId}}", "organizationId": "{{OrderTargetsApiHost.OrgB}}" }
              }
              """);
        byAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Store.Snapshot().Select(t => t.AddedBy).Distinct(StringComparer.Ordinal).Should().Equal(admin.UserId.ToString());
        host.Store.Snapshot().Select(t => t.FeatureId).Should().OnlyContain(id => id.StartsWith("a-pole", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_WithoutAToken_Returns401()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(token: null);

        (await client.PostJsonAsync(Apply, FenceBody())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_ClientAbort_CancelsTheSelectionThroughToTheSpatialQuery_AndWritesNothing()
    {
        var blocking = new BlockingSpatialQuery();
        await using var host = await OrderTargetsApiHost.StartAsync(spatialQuery: blocking);
        await host.SeedOrderAsync();
        using var client = host.ClientFor("admin");
        using var abort = new CancellationTokenSource();

        var call = client.PostAsync(Path(Apply), System.Net.Http.Json.JsonContent.Create(FenceBody()), abort.Token);
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await abort.CancelAsync();

        await FluentActions.Awaiting(() => call).Should().ThrowAsync<OperationCanceledException>();
        (await blocking.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue("the request's token reached the query");
        host.Store.Snapshot().Should().BeEmpty();
    }

    // ── POST /selection/resolve ───────────────────────────────────────────────

    [Fact]
    public async Task Resolve_Fence_ReturnsTheIdsOfTheCallersOrganizationsFeaturesOnly_OrderedById()
    {
        await using var host = await HostAsync();
        using var asA = host.ClientFor("admin");
        using var asB = host.ClientFor("outsider");
        var body = new { strategyId = "polygon-fence", parameters = new { rings = OrderTargetsApiHost.FenceRings() } };

        var forA = await (await asA.PostJsonAsync(Resolve, body)).ReadJsonAsync();
        var forB = await (await asB.PostJsonAsync(Resolve, body)).ReadJsonAsync();

        forA.GetProperty("featureIds").Strings().Should().Equal("a-manhole-1", "a-pole-1", "a-pole-2", "a-pole-3");
        forA.GetProperty("count").GetInt32().Should().Be(4);
        forB.GetProperty("featureIds").Strings().Should().Equal("b-pole-1", "b-pole-2");
    }

    [Fact]
    public async Task Resolve_HonoursTheCallersAssetTypeFilter_AndResolvesAnIdentifierList()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var poles = await (await client.PostJsonAsync(
            Resolve, new { strategyId = "polygon-fence", parameters = new { rings = OrderTargetsApiHost.FenceRings(), assetTypeIds = new[] { "pole" } } })).ReadJsonAsync();
        var byPlate = await (await client.PostJsonAsync(
            Resolve, new { strategyId = "identifier-list", parameters = new { attributeKey = "plate", values = new[] { "P-100", "P-700" } } })).ReadJsonAsync();

        poles.GetProperty("featureIds").Strings().Should().Equal("a-pole-1", "a-pole-2", "a-pole-3");
        byPlate.GetProperty("featureIds").Strings().Should().Equal("a-pole-1");
    }

    [Fact]
    public async Task Resolve_InvalidFenceOrMissingParameter_Returns400InvalidParameters()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        double[][][] unclosed = [[[0, 0], [10, 0], [10, 10], [0, 10]]];

        var invalid = await (await client.PostJsonAsync(Resolve, new { strategyId = "polygon-fence", parameters = new { rings = unclosed } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters);
        await (await client.PostJsonAsync(Resolve, new { strategyId = "polygon-fence", parameters = new { } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters);
        await (await client.PostJsonAsync(Resolve, new { strategyId = "identifier-list", parameters = new { values = new[] { "P-1" } } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.InvalidSelectionParameters);

        invalid.GetProperty("defect").GetString().Should().Be("NotClosed");
    }

    [Fact]
    public async Task Resolve_MoreFeaturesThanTheResolveCeiling_Returns422SelectionTooLarge()
    {
        await using var host = await HostAsync(configure: o => o.MaxResolved = 3);
        using var client = host.ClientFor("admin");

        var problem = await (await client.PostJsonAsync(
                Resolve, new { strategyId = "polygon-fence", parameters = new { rings = OrderTargetsApiHost.FenceRings() } }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.SelectionTooLarge);

        problem.GetProperty("ceiling").GetInt32().Should().Be(3);
    }

    [Theory]
    [InlineData("""{ "strategyId": "no-such-strategy", "parameters": {} }""", WorkflowProblemCodes.UnknownSelectionStrategy)]
    [InlineData("""{ "strategyId": "bounding-box", "parameters": {} }""", WorkflowProblemCodes.UnknownSelectionStrategy)]
    [InlineData("""{ "parameters": {} }""", WorkflowProblemCodes.ValidationFailed)]
    [InlineData("""[]""", WorkflowProblemCodes.ValidationFailed)]
    public async Task Resolve_UnknownOrUnsupportedStrategyOrMalformedBody_Returns400(string body, string code)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(code);
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        await (await client.PostRawAsync(Resolve, body)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task Resolve_RequiresAuthenticationAndTheFeatureReadPermission()
    {
        await using var host = await HostAsync();
        var body = new { strategyId = "polygon-fence", parameters = new { rings = OrderTargetsApiHost.FenceRings() } };
        using var anonymous = host.ClientFor(token: null);
        using var withoutFeatureRead = host.ClientFor("reader"); // holds serviceorders:read only

        (await anonymous.PostJsonAsync(Resolve, body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await withoutFeatureRead.PostJsonAsync(Resolve, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Every route ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryMappedRoute_RejectsARequestWithoutAToken_With401()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(token: null);
        var routes = host.Server.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        routes.Should().HaveCount(10, "the route table of XD01-308 has ten routes");
        foreach (var route in routes)
        {
            var method = route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single();
            var path = route.RoutePattern.RawText!
                .Replace("{id}", "ord-1", StringComparison.Ordinal)
                .Replace("{targetId}", "t-1", StringComparison.Ordinal);
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { }),
            };

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "{0} {1} must not be reachable anonymously", method, path);
        }
    }

    // ── Doubles ───────────────────────────────────────────────────────────────

    /// <summary>Never answers: completes only by observing the caller's cancellation.</summary>
    private sealed class BlockingSpatialQuery : IAssetSpatialQuery
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<GeoFeature>> FindCoveredByAsync(GeoPolygon area, AssetSpatialFilter filter, CancellationToken ct) => BlockAsync(ct);

        public Task<IReadOnlyList<GeoFeature>> FindByAttributeValuesAsync(
            string attributeKey, IReadOnlyCollection<string> values, AssetSpatialFilter filter, CancellationToken ct) => BlockAsync(ct);

        private async Task<IReadOnlyList<GeoFeature>> BlockAsync(CancellationToken ct)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return [];
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult(true);
                throw;
            }
        }
    }

    /// <summary>An order store whose order was changed by someone else: every update loses the version check.</summary>
    private sealed class StaleOrderRepository(IServiceOrderRepository inner) : IServiceOrderRepository
    {
        public event EventHandler<IServiceOrder>? OrderAdded { add => inner.OrderAdded += value; remove => inner.OrderAdded -= value; }
        public event EventHandler<IServiceOrder>? OrderUpdated { add => inner.OrderUpdated += value; remove => inner.OrderUpdated -= value; }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add => inner.OrderStatusChanged += value; remove => inner.OrderStatusChanged -= value; }
        public event EventHandler<string>? OrderDeleted { add => inner.OrderDeleted += value; remove => inner.OrderDeleted -= value; }

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default) =>
            Task.FromException(new ServiceOrderConcurrencyException(order.Id));

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
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => inner.AppendDispatchAsync(orderId, dispatch, ct);
        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default) => inner.AppendActionAsync(orderId, entry, ct);
        public Task DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
    }
}
