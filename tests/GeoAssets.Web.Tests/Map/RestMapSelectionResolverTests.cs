using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Web.Services.Map;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Web.Tests.Map;

/// <summary>The REST adapter behind <see cref="IMapSelectionResolver"/> (XD01-310): what it
/// posts for a box and a fence, and how the server's problem responses surface.</summary>
public class RestMapSelectionResolverTests
{
    private sealed class RecordingHandler(HttpStatusCode status, string body, string mediaType = "application/json") : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri, string Body)> Requests { get; } = [];
        public CancellationToken LastToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            var content = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri, content));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        }
    }

    private static (RestMapSelectionResolver Sut, RecordingHandler Handler) Sut(
        HttpStatusCode status = HttpStatusCode.OK, string body = """{"featureIds":["a","b"],"count":2}""", string mediaType = "application/json")
    {
        var handler = new RecordingHandler(status, body, mediaType);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://server.test/api/workflow/") };
        return (new RestMapSelectionResolver(new RestMapSelectionClient(http)), handler);
    }

    private static string Problem(string code, int status, string extra = "") =>
        $$"""{"type":"urn:geoassets:problem:{{code}}","title":"t","status":{{status}},"code":"{{code}}","detail":"d"{{(extra.Length == 0 ? "" : "," + extra)}}}""";

    private static MapSelectionGesture Box(bool additive = false) => new(MapSelectionTool.Box, [[[-69.5, 19.75], [-70.5, 18.25]]], additive);

    private static double[][][] PostedRings(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("strategyId").GetString().Should().Be("polygon-fence");
        root.GetProperty("parameters").EnumerateObject().Select(p => p.Name).Should().Equal("rings");
        return root.GetProperty("parameters").GetProperty("rings").Deserialize<double[][][]>()!;
    }

    [Fact]
    public async Task Box_PostsAClosedLonLatRectangle_AsPolygonFence_AndReturnsTheIds()
    {
        var (sut, handler) = Sut();

        var ids = await sut.ResolveAsync(Box(), CancellationToken.None);

        ids.Should().Equal("a", "b");
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("https://server.test/api/workflow/selection/resolve"));
        PostedRings(request.Body).Should().ContainSingle().Which.Should().BeEquivalentTo(
            new double[][] { [-70.5, 18.25], [-69.5, 18.25], [-69.5, 19.75], [-70.5, 19.75], [-70.5, 18.25] },
            o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Fence_PostsItsRing_ClosedWhenTheDrawingLeftItOpen()
    {
        var (sut, handler) = Sut();
        var fence = new MapSelectionGesture(MapSelectionTool.Fence, [[[-70, 18], [-69, 18], [-69, 19]]], Additive: true);

        await sut.ResolveAsync(fence, CancellationToken.None);

        PostedRings(handler.Requests.Single().Body).Should().ContainSingle().Which.Should().BeEquivalentTo(
            new double[][] { [-70, 18], [-69, 18], [-69, 19], [-70, 18] }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task TheAdditiveModifier_IsNotSent_ItOnlyDecidesHowTheCallerAppliesTheResult()
    {
        var (plain, plainHandler) = Sut();
        var (additive, additiveHandler) = Sut();

        await plain.ResolveAsync(Box(additive: false), CancellationToken.None);
        await additive.ResolveAsync(Box(additive: true), CancellationToken.None);

        additiveHandler.Requests.Single().Body.Should().Be(plainHandler.Requests.Single().Body);
    }

    [Fact]
    public async Task InvalidGesture_IsRejectedLocally_WithoutARequest()
    {
        var (sut, handler) = Sut();
        var bowTie = new MapSelectionGesture(MapSelectionTool.Fence, [[[-70, 18], [-69, 19], [-69, 18], [-70, 19]]], false);

        var act = () => sut.ResolveAsync(bowTie, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidSelectionFenceException>()).Which.Defect.Should().Be(SelectionFenceDefect.SelfIntersecting);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ServerTooLarge_SurfacesAsSelectionTooLargeException_WithTheCeiling()
    {
        var (sut, _) = Sut(HttpStatusCode.UnprocessableEntity,
            Problem("selection.too-large", 422, "\"ceiling\":10000,\"matchedAtLeast\":10001"), "application/problem+json");

        var act = () => sut.ResolveAsync(Box(), CancellationToken.None);

        (await act.Should().ThrowAsync<SelectionTooLargeException>()).Which.Should().BeEquivalentTo(new { Ceiling = 10_000, MatchedAtLeast = 10_001 });
    }

    [Fact]
    public async Task ServerInvalidFence_SurfacesAsInvalidSelectionFenceException_WithTheDefect()
    {
        var (sut, _) = Sut(HttpStatusCode.BadRequest,
            Problem("selection.invalid-parameters", 400, "\"defect\":\"CrossesAntimeridian\""), "application/problem+json");

        var act = () => sut.ResolveAsync(Box(), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidSelectionFenceException>()).Which.Defect.Should().Be(SelectionFenceDefect.CrossesAntimeridian);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public Task ServerFailureWithoutAProblemBody_SurfacesAsHttpRequestException(HttpStatusCode status)
    {
        var (sut, _) = Sut(status, string.Empty, "text/plain");

        var act = () => sut.ResolveAsync(Box(), CancellationToken.None);

        return act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CancellationToken_IsForwardedToTheRequest()
    {
        var (sut, handler) = Sut();
        using var cts = new CancellationTokenSource();

        await sut.ResolveAsync(Box(), cts.Token);
        handler.LastToken.CanBeCanceled.Should().BeTrue();

        await cts.CancelAsync();
        var act = () => sut.ResolveAsync(Box(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public Task NullGesture_Throws()
    {
        var (sut, _) = Sut();

        var act = () => sut.ResolveAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }
}
