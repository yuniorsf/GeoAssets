using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using GeoAssets.Shared.Services.Offline;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Offline;

/// <summary>
/// Exercises <see cref="RestFieldServiceSyncClient"/>'s GET/POST wiring (XD01-243) against a
/// hand-written fake <see cref="HttpMessageHandler"/> that captures each request and returns a
/// canned response — no mocking library, consistent with this codebase's precedent.
/// </summary>
public class RestFieldServiceSyncClientTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return responder(request);
        }
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static RestFieldServiceSyncClient Sut(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/field-service/sync/") });

    // ── PullSinceAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task PullSinceAsync_IssuesGetWithSinceUtcQueryParameterInRoundTripFormat()
    {
        var sinceUtc = new DateTime(2026, 3, 14, 9, 26, 53, DateTimeKind.Utc);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));
        var sut = Sut(handler);

        await sut.PullSinceAsync(sinceUtc);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        var expectedQuery = $"sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("O"))}";
        request.RequestUri!.PathAndQuery.Should().Be($"/api/field-service/sync/service-orders?{expectedQuery}");
    }

    [Fact]
    public async Task PullSinceAsync_DeserializesJsonArrayIntoServiceOrders()
    {
        const string json = """
            [
                { "id": "order-1", "status": "Pending" },
                { "id": "order-2", "status": "Dispatched" }
            ]
            """;
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(json));
        var sut = Sut(handler);

        var result = await sut.PullSinceAsync(new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc));

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("order-1");
        result[0].Status.Should().Be("Pending");
        result[1].Id.Should().Be("order-2");
        result[1].Status.Should().Be("Dispatched");
    }

    [Fact]
    public async Task PullSinceAsync_ServerReturnsJsonNull_ReturnsEmptyListNotNull()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("null"));
        var sut = Sut(handler);

        var result = await sut.PullSinceAsync(new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc));

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    // ── PushAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PushAsync_IssuesPostToServiceOrdersWithOrdersSerializedAsJsonArrayBody()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));
        var sut = Sut(handler);
        var order = new ServiceOrder { Id = "order-1", Status = FieldWorkStatus.Pending };

        await sut.PushAsync([order]);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.PathAndQuery.Should().Be("/api/field-service/sync/service-orders");
        var body = handler.RequestBodies.Should().ContainSingle().Subject;
        body.Should().Contain("order-1");
    }

    [Fact]
    public async Task PushAsync_DeserializesMixOfSuccessAndFailureResults()
    {
        const string json = """
            [
                { "id": "order-1", "success": true, "error": null },
                { "id": "order-2", "success": false, "error": "some message" }
            ]
            """;
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(json));
        var sut = Sut(handler);
        var orders = new[]
        {
            new ServiceOrder { Id = "order-1" },
            new ServiceOrder { Id = "order-2" },
        };

        var results = await sut.PushAsync(orders);

        results.Should().HaveCount(2);
        results[0].Should().Be(new RemoteSyncPushResult("order-1", true, null));
        results[1].Should().Be(new RemoteSyncPushResult("order-2", false, "some message"));
    }

    [Fact]
    public Task PushAsync_ServerRespondsWithErrorStatusCode_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = Sut(handler);
        var order = new ServiceOrder { Id = "order-1" };

        var act = () => sut.PushAsync([order]);

        return act.Should().ThrowAsync<HttpRequestException>();
    }
}
