using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Provider.Rest.Tests;

public sealed class RestGeoCoreClientTests
{
    private static GeoFeature SampleFeature(string id) => new()
    {
        Id = id,
        Geometry = new GeoPoint(-38.5014, -12.9714),
        Properties = new GeoFeatureProperties { Name = id, AssetTypeId = "pole" }
    };

    [Fact]
    public async Task GetFeatureByIdAsync_WithTokenProvider_AttachesBearerTokenPerRequest()
    {
        using var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(SampleFeature("pole-1")) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://core.geoassets.internal/") };
        var tokenProvider = new FakeServiceAuthTokenProvider("fake-service-token");
        var sut = new RestGeoCoreClient(http, tokenProvider);

        await sut.GetFeatureByIdAsync("pole-1");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Authorization.Should().NotBeNull();
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("fake-service-token");
    }

    [Fact]
    public async Task GetFeatureByIdAsync_WithoutTokenProvider_SendsNoAuthorizationHeader()
    {
        // Regression: omitting the token provider must behave exactly as it did before XD01-299
        // (no header at all), not silently send an empty/malformed one.
        using var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(SampleFeature("pole-1")) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://core.geoassets.internal/") };
        var sut = new RestGeoCoreClient(http);

        await sut.GetFeatureByIdAsync("pole-1");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task QueryFeaturesAsync_WithTokenProvider_AcquiresAFreshTokenOnEachCall()
    {
        // Non-leakage: a stale/cached header on the shared HttpClient would be wrong under
        // concurrent requests — each send must ask the provider again (the provider itself is
        // responsible for caching, not this client).
        using var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PagedResult<GeoFeature> { Items = [], TotalCount = 0 })
            });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://core.geoassets.internal/") };
        var tokenProvider = new FakeServiceAuthTokenProvider("token-1", "token-2");
        var sut = new RestGeoCoreClient(http, tokenProvider);

        await sut.QueryFeaturesAsync(new AssetQuery());
        await sut.QueryFeaturesAsync(new AssetQuery());

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("token-1");
        handler.Requests[1].Headers.Authorization!.Parameter.Should().Be("token-2");
        tokenProvider.CallCount.Should().Be(2);
    }

    [Theory]
    [InlineData("""{"items":[],"totalCount":100001,"totalCountCapped":true}""", 100_001, true)]
    [InlineData("""{"items":[],"totalCount":42}""", 42, false)]
    public async Task QueryFeaturesAsync_ReadsTheServersTotal_CappedOrExact(string body, int expectedTotal, bool expectedCapped)
    {
        // XD01-479: the kernel's features/query counts up to a limit and marks a capped total; an exact one carries no flag.
        using var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://core.geoassets.internal/") };
        var sut = new RestGeoCoreClient(http);

        var page = await sut.QueryFeaturesAsync(new AssetQuery());

        page.TotalCount.Should().Be(expectedTotal);
        page.TotalCountCapped.Should().Be(expectedCapped);
    }
}
