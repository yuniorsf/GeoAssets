using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Provider.Rest.Tests;

public class RestProviderFactoryTests
{
    private static HttpResponseMessage EmptyArrayResponse() =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) };

    [Fact]
    public void Create_RequestsTheNamedGeoAssetsServerClient()
    {
        var handler = new FakeHttpMessageHandler(_ => EmptyArrayResponse());
        var factory = new FakeHttpClientFactory(handler);
        var sut = new RestProviderFactory(factory);

        sut.Create("http://localhost:5000/api/geoassets");

        // Must be the named client, not the anonymous default (""), so a host can attach
        // an auth handler (e.g. AuthorizationMessageHandler in GeoAssets.Web) scoped to
        // exactly this destination — see RestProviderFactory.BuildClient.
        factory.RequestedNames.Should().ContainSingle().Which.Should().Be("GeoAssetsServer");
    }

    [Fact]
    public void Create_SetsBaseAddress_NormalizingTrailingSlash()
    {
        var handler = new FakeHttpMessageHandler(_ => EmptyArrayResponse());
        var factory = new FakeHttpClientFactory(handler);
        var sut = new RestProviderFactory(factory);

        sut.Create("http://localhost:5000/api/geoassets/");

        handler.Requests.Should().BeEmpty(); // Create() alone issues no requests.
    }

    [Fact]
    public async Task CreateAsync_RoutesRequestsThroughTheNamedClient()
    {
        var handler = new FakeHttpMessageHandler(_ => EmptyArrayResponse());
        var factory = new FakeHttpClientFactory(handler);
        var sut = new RestProviderFactory(factory);

        var provider = await sut.CreateAsync("http://localhost:5000/api/geoassets");

        provider.GetAll().Should().BeEmpty();
        factory.RequestedNames.Should().Contain("GeoAssetsServer");
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath)
            .Should().Contain(["/api/geoassets/features", "/api/geoassets/asset-types"]);
    }
}
