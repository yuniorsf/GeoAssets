using System.Net;
using FluentAssertions;
using GeoAssets.Core.Erp.Maximo;
using GeoAssets.FieldService.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

public class HttpMaximoAssetLifecycleDispatcherTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static IConfiguration ConfigWithUrl(string? url) => new ConfigurationBuilder()
        .AddInMemoryCollection(url is null
            ? []
            : new Dictionary<string, string?>(StringComparer.Ordinal) { ["Maximo:Mif:AssetLifecycleUrl"] = url })
        .Build();

    private static readonly FakeTimeProvider Clock = new();

    private static MaximoAssetLifecycleUpdate SampleUpdate() => new(
        BatchId: Guid.NewGuid(),
        OldSerial: "SN-OLD",
        NewSerial: "SN-NEW",
        LocationFeatureId: "feature-new",
        OrganizationId: Guid.NewGuid(),
        SourceOrderId: "order-1",
        SyncedAtUtc: Clock.GetUtcNow());

    [Fact]
    public async Task DispatchAsync_ConfiguredUrlAndSuccessResponse_PostsToConfiguredUrl()
    {
        const string url = "https://maximo.example.test/mif/asset-lifecycle";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var dispatcher = new HttpMaximoAssetLifecycleDispatcher(new FakeHttpClientFactory(handler), ConfigWithUrl(url));

        await dispatcher.DispatchAsync(SampleUpdate());

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri.Should().Be(new Uri(url));
    }

    [Fact]
    public async Task DispatchAsync_NoUrlConfigured_ThrowsWithoutAttemptingHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var dispatcher = new HttpMaximoAssetLifecycleDispatcher(new FakeHttpClientFactory(handler), ConfigWithUrl(null));

        var act = async () => await dispatcher.DispatchAsync(SampleUpdate());

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public Task DispatchAsync_NonSuccessStatusCode_Throws()
    {
        const string url = "https://maximo.example.test/mif/asset-lifecycle";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var dispatcher = new HttpMaximoAssetLifecycleDispatcher(new FakeHttpClientFactory(handler), ConfigWithUrl(url));

        var act = async () => await dispatcher.DispatchAsync(SampleUpdate());

        return act.Should().ThrowAsync<HttpRequestException>();
    }
}
