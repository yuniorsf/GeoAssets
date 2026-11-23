using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Services.ReferenceLayers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.ReferenceLayers;

/// <summary>
/// XD01-331 — <see cref="RestReferenceFeatureClient"/> against a hand-written fake handler, and its registration
/// (<see cref="ReferenceFeaturesServiceExtensions.AddReferenceFeaturesRest"/>).
/// </summary>
public sealed class RestReferenceFeatureClientTests
{
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000331");
    private static readonly BoundingBox Box = new(-38.6, -13.05, -38.4, -12.8);

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

    private static HttpResponseMessage Response(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static RestReferenceFeatureClient Sut(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://gateway.example/api/geoassets/") });

    [Fact]
    public async Task GetAsync_AsksTheReferenceFeaturesRouteOfTheOrganization_ForTheBox()
    {
        var handler = new FakeHttpMessageHandler(_ => Response(HttpStatusCode.OK, "[]"));

        await Sut(handler).GetAsync(Coelba, Box, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.AbsolutePath.Should().Be($"/api/geoassets/organizations/{Coelba:D}/reference-features");
        request.RequestUri.Query.Should().Be("?bbox=-38.6,-13.05,-38.4,-12.8");
    }

    [Fact]
    public async Task GetAsync_ForwardsTheCallersToken_SoASupersededLoadIsAborted()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new FakeHttpMessageHandler(_ => Response(HttpStatusCode.OK, "[]"));

        await Record.ExceptionAsync(() => Sut(handler).GetAsync(Coelba, Box, cts.Token));

        handler.Tokens.Should().ContainSingle().Which.IsCancellationRequested
            .Should().BeTrue("the handler pipeline sees the caller's cancellation (HttpClient links it with its timeout)");
    }

    [Fact]
    public async Task GetAsync_WritesInvariantNumbers_WhateverTheCurrentCulture()
    {
        var handler = new FakeHttpMessageHandler(_ => Response(HttpStatusCode.OK, "[]"));
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            await Sut(handler).GetAsync(Coelba, Box, CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        handler.Requests.Single().RequestUri!.Query.Should().Be("?bbox=-38.6,-13.05,-38.4,-12.8");
    }

    [Fact]
    public async Task GetAsync_Ok_ReturnsTheBodyAsWritten()
    {
        const string body = """[{"id":"p1","geometry":{"type":"Point","coordinates":[-38.5,-12.9]},"label":"P1","assetTypeId":"t","assetTypeName":""}]""";
        var handler = new FakeHttpMessageHandler(_ => Response(HttpStatusCode.OK, body));

        var response = await Sut(handler).GetAsync(Coelba, Box, CancellationToken.None);

        response.IsForbidden.Should().BeFalse();
        response.FeaturesJson.Should().Be(body);
    }

    [Fact]
    public async Task GetAsync_OkWithAnEmptyBody_IsAnEmptyArray()
    {
        var handler = new FakeHttpMessageHandler(_ => Response(HttpStatusCode.OK, " "));

        (await Sut(handler).GetAsync(Coelba, Box, CancellationToken.None)).FeaturesJson.Should().Be("[]");
    }

    [Fact]
    public async Task GetAsync_Forbidden_IsTheRefusal_NotAnError()
    {
        var handler = new FakeHttpMessageHandler(_ => Response(HttpStatusCode.Forbidden, """{"reason":"Not authorized"}"""));

        var response = await Sut(handler).GetAsync(Coelba, Box, CancellationToken.None);

        response.Should().BeSameAs(ReferenceFeatureResponse.Forbidden);
        response.IsForbidden.Should().BeTrue();
        response.FeaturesJson.Should().Be("[]");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public Task GetAsync_AnyOtherFailure_Throws(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler(_ => Response(status));

        return FluentActions.Awaiting(() => Sut(handler).GetAsync(Coelba, Box, CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void Granted_RejectsANullBody() =>
        FluentActions.Invoking(() => ReferenceFeatureResponse.Granted(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public async Task TheUnavailableClient_RefusesEveryOrganization()
    {
        var response = await new UnavailableReferenceFeatureClient().GetAsync(Coelba, Box, CancellationToken.None);

        response.IsForbidden.Should().BeTrue();
    }

    // ── Registration ──────────────────────────────────────────────────────────

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public List<string> Names { get; } = [];
        public List<HttpClient> Clients { get; } = [];

        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            var client = new HttpClient();
            Clients.Add(client);
            return client;
        }
    }

    [Theory]
    [InlineData("https://gateway.example")]
    [InlineData("https://gateway.example/")]
    public void AddReferenceFeaturesRest_UsesTheTokenCarryingServerClient_BasedAtTheGeoAssetsApi(string baseUrl)
    {
        var factory = new RecordingHttpClientFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddReferenceFeaturesRest(baseUrl);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IReferenceFeatureClient>().Should().BeOfType<RestReferenceFeatureClient>();

        factory.Names.Should().Equal("GeoAssetsServer");
        factory.Clients.Single().BaseAddress.Should().Be(new Uri("https://gateway.example/api/geoassets/"));
    }

    [Fact]
    public void AddReferenceFeaturesRest_RejectsABlankBaseUrl() =>
        FluentActions.Invoking(() => new ServiceCollection().AddReferenceFeaturesRest(" "))
            .Should().Throw<ArgumentException>();
}
