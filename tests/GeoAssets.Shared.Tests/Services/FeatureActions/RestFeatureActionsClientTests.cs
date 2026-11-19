using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using GeoAssets.Shared.Services.FeatureActions;
using GeoAssets.Workflow.Registry;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.FeatureActions;

/// <summary>Exercises <see cref="RestFeatureActionsClient"/> against a hand-written fake handler (XD01-370).</summary>
public class RestFeatureActionsClientTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Tokens.Add(cancellationToken);
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static RestFeatureActionsClient Sut(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/geoassets/") });

    [Fact]
    public async Task GetActionsAsync_IssuesGetAgainstTheFeatureActionsPath()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));

        await Sut(handler).GetActionsAsync("pole-1", CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsolutePath.Should().Be("/api/geoassets/features/pole-1/actions");
    }

    [Fact]
    public async Task GetActionsAsync_EscapesTheFeatureId()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));

        await Sut(handler).GetActionsAsync("a/b c?d", CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.RequestUri!.AbsoluteUri
            .Should().Be("http://localhost/api/geoassets/features/a%2Fb%20c%3Fd/actions");
    }

    [Fact]
    public async Task GetActionsAsync_ParsesCamelCaseJsonIntoDescriptors()
    {
        const string json = """
            [
                { "role": "pole", "verb": "create", "labelKey": "featureActions.createSharedUseOrder", "orderTypeId": "shared-use-pole-work", "route": "service-orders" },
                { "role": "pole", "verb": "inspect", "labelKey": "k2", "orderTypeId": "t2", "route": "r2" }
            ]
            """;
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(json));

        var actions = await Sut(handler).GetActionsAsync("pole-1", CancellationToken.None);

        actions.Should().Equal(
            new FeatureActionDescriptor("pole", "create", "featureActions.createSharedUseOrder", "shared-use-pole-work", "service-orders"),
            new FeatureActionDescriptor("pole", "inspect", "k2", "t2", "r2"));
    }

    [Fact]
    public async Task GetActionsAsync_NullBody_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("null"));

        var actions = await Sut(handler).GetActionsAsync("pole-1", CancellationToken.None);

        actions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActionsAsync_NotFound_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var actions = await Sut(handler).GetActionsAsync("missing", CancellationToken.None);

        actions.Should().BeEmpty();
    }

    [Fact]
    public Task GetActionsAsync_ServerError_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = () => Sut(handler).GetActionsAsync("pole-1", CancellationToken.None);

        return act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetActionsAsync_BlankId_ThrowsArgumentExceptionWithoutRequest(string id)
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));

        var act = () => Sut(handler).GetActionsAsync(id, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActionsAsync_ForwardsTheCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var handler = new BlockingHandler();

        var pending = Sut(handler).GetActionsAsync("pole-1", cts.Token);
        await handler.Started.Task;
        await cts.CancelAsync();

        var act = () => pending;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
