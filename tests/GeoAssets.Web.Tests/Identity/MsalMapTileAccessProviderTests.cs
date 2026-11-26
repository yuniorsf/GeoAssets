using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Web.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>
/// XD01-96: a WMS layer on the gateway gets the token of the scope its route carries, as the "GeoAssetsServer" client's
/// requests do; a layer anywhere else gets none, and the token provider is never asked for it.
/// </summary>
public sealed class MsalMapTileAccessProviderTests
{
    private const string Kernel = "api://kernel/access_as_user";
    private const string NeutralOperator = "api://no/access_as_user";
    private static readonly DateTimeOffset Expiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeTokenProvider(bool grant = true) : IAccessTokenProvider
    {
        public List<string[]> Requested { get; } = [];

        public ValueTask<AccessTokenResult> RequestAccessToken() => throw new NotSupportedException();

        public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options)
        {
            Requested.Add(options.Scopes?.ToArray() ?? []);
            var token = grant ? new AccessToken { Value = $"token-for:{options.Scopes!.Single()}", Expires = Expiry } : null;
            return ValueTask.FromResult(new AccessTokenResult(
                grant ? AccessTokenResultStatus.Success : AccessTokenResultStatus.RequiresRedirect,
                token!, grant ? null : "authentication/login", null));
        }
    }

    private static (MsalMapTileAccessProvider Provider, FakeTokenProvider Tokens) Build(bool grant = true, string? defaultScope = Kernel)
    {
        var tokens = new FakeTokenProvider(grant);
        var routes = new ApiScopeRoutes(new Uri("https://gw/"), defaultScope, [new ApiScopeRoute("/api/v/neutral-operator/", NeutralOperator)]);
        return (new MsalMapTileAccessProvider(tokens, routes), tokens);
    }

    [Theory]
    [InlineData("https://gw/wms")]
    [InlineData("https://gw/api/geoassets/wms")]
    public async Task AKernelWmsLayer_GetsTheDefaultScopesToken_WithItsExpiry(string url)
    {
        var (provider, tokens) = Build();

        var access = await provider.GetAccessAsync(new Uri(url));

        access.Should().Be(MapTileAccess.Bearer($"token-for:{Kernel}", Expiry));
        tokens.Requested.Should().ContainSingle().Which.Should().Equal(Kernel);
    }

    [Fact]
    public async Task ALayerUnderAConfiguredRoute_GetsThatRoutesToken()
    {
        var (provider, tokens) = Build();

        var access = await provider.GetAccessAsync(new Uri("https://gw/api/v/neutral-operator/wms"));

        access.Should().Be(MapTileAccess.Bearer($"token-for:{NeutralOperator}", Expiry));
        tokens.Requested.Should().ContainSingle().Which.Should().Equal(NeutralOperator);
    }

    [Theory]
    [InlineData("https://geoserver.example/geoserver/wms")]
    [InlineData("http://gw/wms")]
    [InlineData("https://gw:8443/wms")]
    [InlineData("https://gw.example/wms")]
    public async Task ALayerOnAnotherServer_IsLoadedWithoutAToken_AndNoTokenIsRequested(string url)
    {
        var (provider, tokens) = Build();

        var access = await provider.GetAccessAsync(new Uri(url));

        access.Should().Be(MapTileAccess.Anonymous);
        tokens.Requested.Should().BeEmpty("a token for this app's API must never be handed to another host");
    }

    [Fact]
    public async Task AGatewayLayerWithNoScopeConfigured_IsUnavailable()
    {
        var (provider, tokens) = Build(defaultScope: null);

        var access = await provider.GetAccessAsync(new Uri("https://gw/wms"));

        access.Should().Be(MapTileAccess.Unavailable);
        tokens.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenThatNeedsInteraction_IsUnavailable_WithoutThrowing()
    {
        var (provider, _) = Build(grant: false);

        var access = await provider.GetAccessAsync(new Uri("https://gw/wms"));

        access.Should().Be(MapTileAccess.Unavailable);
    }

    [Fact]
    public async Task ARelativeUrl_IsLoadedWithoutAToken()
    {
        var (provider, tokens) = Build();

        var access = await provider.GetAccessAsync(new Uri("/wms", UriKind.Relative));

        access.Should().Be(MapTileAccess.Anonymous);
        tokens.Requested.Should().BeEmpty();
    }

    [Fact]
    public Task NullArguments_AreRefused()
    {
        var (provider, tokens) = Build();
        var routes = new ApiScopeRoutes(new Uri("https://gw/"), Kernel);

        FluentActions.Invoking(() => new MsalMapTileAccessProvider(null!, routes)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new MsalMapTileAccessProvider(tokens, null!)).Should().Throw<ArgumentNullException>();
        return FluentActions.Awaiting(() => provider.GetAccessAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }
}
