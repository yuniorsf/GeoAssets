using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Provider.Rest.Tests;

public sealed class RestOrganizationScopedGeoCoreClientTests
{
    private static readonly Guid OrganizationId = Guid.Parse("0a1b2c3d-0000-0000-0000-00000000abcd");
    private const string OrganizationSegment = "0a1b2c3d-0000-0000-0000-00000000abcd";
    private static readonly Uri BaseAddress = new("https://core.geoassets.internal/api/geoassets/");

    private static GeoFeature SampleFeature(string id) => new()
    {
        Id = id,
        Geometry = new GeoPoint(-38.5014, -12.9714),
        Properties = new GeoFeatureProperties { Name = id, AssetTypeId = "pole" }
    };

    private static HttpResponseMessage Page(int skip, int take, int total, string prefix = "f")
    {
        var items = Enumerable.Range(skip, Math.Max(0, Math.Min(take, total - skip)))
            .Select(i => SampleFeature($"{prefix}-{i:D5}"))
            .ToList();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PagedResult<GeoFeature> { Items = items, TotalCount = total })
        };
    }

    private static int QueryInt(Uri uri, string key)
    {
        var pair = uri.Query.TrimStart('?').Split('&').Single(p => p.StartsWith(key + "=", StringComparison.Ordinal));
        return int.Parse(pair[(key.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static HttpClient NewHttp(HttpMessageHandler handler) => new(handler) { BaseAddress = BaseAddress };

    [Fact]
    public async Task GetFeatureAsync_BuildsOrganizationScopedUrlWithDFormatAndEscapedId()
    {
        using var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(SampleFeature("a/b c")) });
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var feature = await sut.GetFeatureAsync(OrganizationId, "a/b c");

        feature.Should().NotBeNull();
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsoluteUri.Should().Be(
            $"https://core.geoassets.internal/api/geoassets/organizations/{OrganizationSegment}/features/a%2Fb%20c");
    }

    [Fact]
    public async Task GetFeatureAsync_NotFound_ReturnsNull()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var feature = await sut.GetFeatureAsync(OrganizationId, "missing");

        feature.Should().BeNull();
    }

    [Fact]
    public async Task GetFeatureAsync_Forbidden_ThrowsUnauthorizedAccessException()
    {
        using var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("service caller only") });
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var act = () => sut.GetFeatureAsync(OrganizationId, "f1");

        (await act.Should().ThrowAsync<UnauthorizedAccessException>()).WithMessage("service caller only");
    }

    [Fact]
    public async Task GetFeatureAsync_ServerError_ThrowsHttpRequestException()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var act = () => sut.GetFeatureAsync(OrganizationId, "f1");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetFeatureAsync_WithTokenProvider_AcquiresAFreshBearerTokenPerRequest()
    {
        using var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(SampleFeature("f1")) });
        using var http = NewHttp(handler);
        var tokens = new FakeServiceAuthTokenProvider("token-1", "token-2");
        var sut = new RestOrganizationScopedGeoCoreClient(http, tokens);

        await sut.GetFeatureAsync(OrganizationId, "f1");
        await sut.GetFeatureAsync(OrganizationId, "f1");

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("token-1");
        handler.Requests[1].Headers.Authorization!.Parameter.Should().Be("token-2");
        tokens.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetFeatureAsync_WithoutTokenProvider_SendsNoAuthorizationHeader()
    {
        using var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(SampleFeature("f1")) });
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        await sut.GetFeatureAsync(OrganizationId, "f1");

        handler.Requests.Single().Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task GetDescendantsAsync_BuildsUrlWithEscapedEdgeKindSkipAndTake()
    {
        using var handler = new FakeHttpMessageHandler(_ => Page(0, 500, 1));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var result = await sut.GetDescendantsAsync(OrganizationId, "sw 1", "feeds&more");

        result.Should().HaveCount(1);
        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().Be(
            $"https://core.geoassets.internal/api/geoassets/organizations/{OrganizationSegment}/features/sw%201/descendants?edgeKind=feeds%26more&skip=0&take=500");
    }

    [Fact]
    public async Task GetDescendantsAsync_PagesUntilTotalCountReached()
    {
        using var handler = new FakeHttpMessageHandler(request =>
            Page(QueryInt(request.RequestUri!, "skip"), QueryInt(request.RequestUri!, "take"), total: 1_200));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var result = await sut.GetDescendantsAsync(OrganizationId, "sw1", "feeds");

        result.Should().HaveCount(1_200);
        result.Select(f => f.Id).Should().OnlyHaveUniqueItems();
        handler.Requests.Select(r => QueryInt(r.RequestUri!, "skip")).Should().Equal(0, 500, 1_000);
    }

    [Fact]
    public async Task GetDescendantsAsync_StopsAtTenThousandFeatures()
    {
        using var handler = new FakeHttpMessageHandler(request =>
            Page(QueryInt(request.RequestUri!, "skip"), QueryInt(request.RequestUri!, "take"), total: 50_000));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var result = await sut.GetDescendantsAsync(OrganizationId, "sw1", "feeds");

        result.Should().HaveCount(RestOrganizationScopedGeoCoreClient.MaxFeatures);
        handler.Requests.Should().HaveCount(RestOrganizationScopedGeoCoreClient.MaxFeatures / RestOrganizationScopedGeoCoreClient.PageSize);
    }

    [Fact]
    public async Task GetDescendantsAsync_EmptyPageBeforeTotalCount_Stops()
    {
        using var handler = new FakeHttpMessageHandler(_ => Page(0, 0, total: 10));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var result = await sut.GetDescendantsAsync(OrganizationId, "sw1", "feeds");

        result.Should().BeEmpty();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task GetDescendantsAsync_NotFound_ReturnsEmpty()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var result = await sut.GetDescendantsAsync(OrganizationId, "missing", "feeds");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDescendantsAsync_Forbidden_ThrowsUnauthorizedAccessException()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var act = () => sut.GetDescendantsAsync(OrganizationId, "sw1", "feeds");

        (await act.Should().ThrowAsync<UnauthorizedAccessException>()).WithMessage("Not authorized.");
    }

    [Fact]
    public async Task GetDescendantsAsync_WithTokenProvider_SendsBearerTokenOnEveryPage()
    {
        using var handler = new FakeHttpMessageHandler(request =>
            Page(QueryInt(request.RequestUri!, "skip"), QueryInt(request.RequestUri!, "take"), total: 600));
        using var http = NewHttp(handler);
        var tokens = new FakeServiceAuthTokenProvider("t1", "t2");
        var sut = new RestOrganizationScopedGeoCoreClient(http, tokens);

        await sut.GetDescendantsAsync(OrganizationId, "sw1", "feeds");

        handler.Requests.Select(r => r.Headers.Authorization!.Parameter).Should().Equal("t1", "t2");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetFeatureAsync_BlankId_Throws(string id)
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var act = () => sut.GetFeatureAsync(OrganizationId, id);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDescendantsAsync_BlankEdgeKind_Throws()
    {
        using var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = NewHttp(handler);
        var sut = new RestOrganizationScopedGeoCoreClient(http);

        var act = () => sut.GetDescendantsAsync(OrganizationId, "sw1", " ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://core.geoassets.internal/api/geoassets")]
    [InlineData("/relative/api/geoassets")]
    public void AddOrganizationScopedGeoCoreClient_BadBaseUrl_Throws(string baseUrl)
    {
        var services = new ServiceCollection();

        var act = () => services.AddOrganizationScopedGeoCoreClient(baseUrl);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddOrganizationScopedGeoCoreClient_BlankBaseUrl_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddOrganizationScopedGeoCoreClient(" ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AddOrganizationScopedGeoCoreClient_ResolvesClientBoundToBaseUrlAndTokenProvider()
    {
        using var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(SampleFeature("f1")) });
        var services = new ServiceCollection();
        services.AddSingleton<GeoAssets.Identity.Authentication.IServiceAuthTokenProvider>(new FakeServiceAuthTokenProvider("svc-token"));
        services.AddOrganizationScopedGeoCoreClient("https://core.geoassets.internal/api/geoassets");
        services.AddHttpClient("GeoAssetsServer:organization-scoped").ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IOrganizationScopedGeoCoreClient>();
        var feature = await client.GetFeatureAsync(OrganizationId, "f1");

        client.Should().BeOfType<RestOrganizationScopedGeoCoreClient>();
        feature.Should().NotBeNull();
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.AbsoluteUri.Should().Be(
            $"https://core.geoassets.internal/api/geoassets/organizations/{OrganizationSegment}/features/f1");
        request.Headers.Authorization!.Parameter.Should().Be("svc-token");
    }
}
