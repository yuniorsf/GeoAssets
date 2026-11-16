using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// The requests <see cref="RestOrderTargetSelectionService"/> and
/// <see cref="RestMapSelectionClient"/> send (XD01-308) — above all, that nothing about the
/// caller's identity is on the wire — and the registration of the new clients.
/// </summary>
public sealed class RestOrderTargetSelectionServiceTests
{
    private const string PreviewJson = """
        { "orderTypeId": "pole-survey", "matched": 2, "eligible": 2, "alreadyInOrder": 0, "truncatedByLimit": 0, "warningCount": 1, "blockedCount": 0,
          "firstPage": { "items": [], "totalCount": 2 }, "notFoundValues": ["P-9"], "ambiguousValues": [] }
        """;

    private const string ResultJson = """
        { "orderTypeId": "pole-survey", "added": 2, "matched": 2, "eligible": 2, "alreadyInOrder": 0, "truncatedByLimit": 0, "warningCount": 0, "blockedCount": 0 }
        """;

    private static readonly WorkflowPrincipal Principal = new(
        "user-SECRET-ID", "org-SECRET-ID", ["Administrator"], ["group-SECRET-ID"], ["serviceorders:annotate"]);

    private static HttpClient Http(BodyRecordingHandler handler) => new(handler) { BaseAddress = new Uri("http://test/api/workflow/") };

    private static TargetSelectionRequest Request(string orderId = "ord-1") => new(
        orderId,
        "polygon-fence",
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["rings"] = new double[][][] { [[0, 0], [1, 0], [1, 1], [0, 0]] },
            ["assetTypeIds"] = new[] { "pole" },
        },
        page: 2,
        pageSize: 25,
        acceptTruncation: true);

    [Fact]
    public async Task PreviewAsync_PostsTheSelectionToThePreviewRoute_AndReadsThePreview()
    {
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, PreviewJson));

        var preview = await new RestOrderTargetSelectionService(Http(handler)).PreviewAsync(Request("ord/1"), Principal, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/workflow/service-orders/ord%2F1/targets/selection/preview");
        request.Json.GetProperty("strategyId").GetString().Should().Be("polygon-fence");
        request.Json.GetProperty("page").GetInt32().Should().Be(2);
        request.Json.GetProperty("pageSize").GetInt32().Should().Be(25);
        request.Json.GetProperty("acceptTruncation").GetBoolean().Should().BeTrue();
        request.Json.GetProperty("parameters").GetProperty("rings")[0][1][0].GetDouble().Should().Be(1);
        request.Json.GetProperty("parameters").GetProperty("assetTypeIds")[0].GetString().Should().Be("pole");
        preview.Should().BeEquivalentTo(new { OrderTypeId = "pole-survey", Matched = 2, WarningCount = 1 });
        preview.FirstPage.TotalCount.Should().Be(2);
        preview.NotFoundValues.Should().Equal("P-9");
    }

    [Fact]
    public async Task ApplyAsync_PostsToTheApplyRoute_AndReadsTheResult()
    {
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, ResultJson));

        var result = await new RestOrderTargetSelectionService(Http(handler)).ApplyAsync(Request(), Principal, CancellationToken.None);

        handler.Requests.Single().PathAndQuery.Should().Be("/api/workflow/service-orders/ord-1/targets/selection/apply");
        result.Should().BeEquivalentTo(new { Added = 2, Matched = 2 });
        result.NotFoundValues.Should().BeEmpty();
    }

    [Fact]
    public async Task ThePrincipal_IsNeverSerialised_NotInTheBodyNorInTheUrl()
    {
        var handler = new BodyRecordingHandler(r =>
            BodyRecordingHandler.Json(HttpStatusCode.OK, r.PathAndQuery.EndsWith("preview", StringComparison.Ordinal) ? PreviewJson : ResultJson));
        var sut = new RestOrderTargetSelectionService(Http(handler));

        await sut.PreviewAsync(Request(), Principal, CancellationToken.None);
        await sut.ApplyAsync(Request(), Principal, CancellationToken.None);

        foreach (var request in handler.Requests)
        {
            request.Json.EnumerateObject().Select(p => p.Name)
                .Should().BeEquivalentTo("strategyId", "parameters", "page", "pageSize", "acceptTruncation");
            (request.Body + request.PathAndQuery).Should().NotContain("SECRET-ID").And.NotContain("Administrator");
        }
    }

    [Fact]
    public async Task Failure_IsTranslated_WithTheActionEachCallNeeds()
    {
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied, "\"reason\": \"no\""));
        var sut = new RestOrderTargetSelectionService(Http(handler));

        var preview = () => sut.PreviewAsync(Request(), Principal, CancellationToken.None);
        var apply = () => sut.ApplyAsync(Request(), Principal, CancellationToken.None);

        (await preview.Should().ThrowAsync<OrderActionDeniedException>()).Which.Action.Should().Be(Orders.OrderActionType.View);
        (await apply.Should().ThrowAsync<OrderActionDeniedException>()).Which.Action.Should().Be(Orders.OrderActionType.Annotate);
    }

    [Fact]
    public async Task EmptySuccessBody_ThrowsJsonException_AndGuardsFireBeforeAnyRequest()
    {
        var empty = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, "null"));
        var silent = new BodyRecordingHandler(_ => throw new InvalidOperationException("no request expected"));
        var sut = new RestOrderTargetSelectionService(Http(silent));

        await FluentActions.Invoking(() => new RestOrderTargetSelectionService(Http(empty)).PreviewAsync(Request(), Principal, CancellationToken.None))
            .Should().ThrowAsync<JsonException>();
        await FluentActions.Invoking(() => new RestOrderTargetSelectionService(Http(empty)).ApplyAsync(Request(), Principal, CancellationToken.None))
            .Should().ThrowAsync<JsonException>();
        await FluentActions.Invoking(() => sut.PreviewAsync(null!, Principal, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.ApplyAsync(Request(), null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.ApplyAsync(Request(" "), Principal, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        silent.Requests.Should().BeEmpty();
    }

    // ── RestMapSelectionClient ────────────────────────────────────────────────

    [Fact]
    public async Task MapSelectionClient_PostsTheGestureToSelectionResolve_AndReturnsTheIds()
    {
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, """{ "featureIds": ["a", "b"], "count": 2 }"""));
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = new double[][][] { [[0, 0], [1, 0], [1, 1], [0, 0]] } };

        var ids = await new RestMapSelectionClient(Http(handler)).ResolveAsync("polygon-fence", parameters, CancellationToken.None);

        ids.Should().Equal("a", "b");
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/workflow/selection/resolve");
        request.Json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("strategyId", "parameters");
    }

    [Fact]
    public async Task MapSelectionClient_ResponseWithoutAnIdArray_ThrowsJsonException_AndAProblemIsTranslated()
    {
        var malformed = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, """{ "ids": [] }"""));
        var tooLarge = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(
            HttpStatusCode.UnprocessableEntity, WorkflowProblemCodes.SelectionTooLarge, "\"ceiling\": 10000, \"matchedAtLeast\": 10001"));
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);

        await FluentActions.Invoking(() => new RestMapSelectionClient(Http(malformed)).ResolveAsync("polygon-fence", parameters, CancellationToken.None))
            .Should().ThrowAsync<JsonException>();
        (await FluentActions.Invoking(() => new RestMapSelectionClient(Http(tooLarge)).ResolveAsync("polygon-fence", parameters, CancellationToken.None))
            .Should().ThrowAsync<SelectionTooLargeException>()).Which.Ceiling.Should().Be(10_000);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddWorkflowRest_RegistersTheTargetAndSelectionClients_OnTheNamedGeoAssetsServerClient()
    {
        var handler = new BodyRecordingHandler(r => r.PathAndQuery switch
        {
            var path when path.EndsWith("/targets/count", StringComparison.Ordinal) => BodyRecordingHandler.Json(HttpStatusCode.OK, """{ "count": 4 }"""),
            var path when path.EndsWith("/selection/preview", StringComparison.Ordinal) => BodyRecordingHandler.Json(HttpStatusCode.OK, PreviewJson),
            _ => BodyRecordingHandler.Json(HttpStatusCode.OK, """{ "featureIds": [], "count": 0 }"""),
        });
        var services = new ServiceCollection();
        services.AddHttpClient("GeoAssetsServer").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddWorkflowRest("http://test/api/workflow");
        await using var provider = services.BuildServiceProvider();

        var reader = provider.GetRequiredService<IServiceOrderTargetReader>();
        var writer = provider.GetRequiredService<IServiceOrderTargetWriter>();
        var selection = provider.GetRequiredService<IOrderTargetSelectionService>();
        var map = provider.GetRequiredService<RestMapSelectionClient>();
        await reader.CountAsync("ord-1", CancellationToken.None);
        await selection.PreviewAsync(Request(), Principal, CancellationToken.None);
        await map.ResolveAsync("polygon-fence", new Dictionary<string, object>(StringComparer.Ordinal), CancellationToken.None);

        reader.Should().BeOfType<RestServiceOrderTargetRepository>().And.BeSameAs(writer);
        selection.Should().BeOfType<RestOrderTargetSelectionService>();
        handler.Requests.Select(r => r.PathAndQuery).Should().Equal(
            "/api/workflow/service-orders/ord-1/targets/count",
            "/api/workflow/service-orders/ord-1/targets/selection/preview",
            "/api/workflow/selection/resolve");
    }
}
