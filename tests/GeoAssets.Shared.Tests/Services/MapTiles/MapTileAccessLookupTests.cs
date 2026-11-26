using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.MapTiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.MapTiles;

/// <summary>XD01-96: what the map is told about a WMS layer's tiles, and that a failure never opens a layer up.</summary>
public sealed class MapTileAccessLookupTests
{
    private static readonly DateTimeOffset Expiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeProvider(Func<Uri, MapTileAccess> answer) : IMapTileAccessProvider
    {
        public List<Uri> Asked { get; } = [];

        public Task<MapTileAccess> GetAccessAsync(Uri tileUrl, CancellationToken cancellationToken = default)
        {
            Asked.Add(tileUrl);
            return Task.FromResult(answer(tileUrl));
        }
    }

    private sealed class ThrowingProvider : IMapTileAccessProvider
    {
        public Task<MapTileAccess> GetAccessAsync(Uri tileUrl, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("token cache unavailable");
    }

    [Fact]
    public async Task TheProvidersAnswer_IsPassedThrough_ForTheLayersAbsoluteUrl()
    {
        var provider = new FakeProvider(_ => MapTileAccess.Bearer("token-1", Expiry));

        var access = await MapTileAccessLookup.GetAsync(provider, "https://gw/api/geoassets/wms", NullLogger.Instance);

        access.Should().Be(MapTileAccess.Bearer("token-1", Expiry));
        provider.Asked.Should().Equal(new Uri("https://gw/api/geoassets/wms"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/api/geoassets/wms")]
    [InlineData("not a url")]
    [InlineData("file:///etc/hosts")]
    [InlineData("blob:https://gw/0f1e")]
    public async Task AUrlThatIsNotAnAbsoluteHttpUrl_IsUnavailable_AndTheProviderIsNotAsked(string? tileUrl)
    {
        var provider = new FakeProvider(_ => MapTileAccess.Anonymous);

        var access = await MapTileAccessLookup.GetAsync(provider, tileUrl, NullLogger.Instance);

        access.Should().Be(MapTileAccess.Unavailable);
        provider.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailingProvider_IsUnavailable_NotAnonymous()
    {
        var access = await MapTileAccessLookup.GetAsync(new ThrowingProvider(), "https://gw/wms", NullLogger.Instance);

        access.Should().Be(MapTileAccess.Unavailable);
        access.RequiresToken.Should().BeTrue("a layer whose token source failed must not be requested without a token");
    }

    [Fact]
    public async Task NullArguments_AreRefused()
    {
        var provider = new FakeProvider(_ => MapTileAccess.Anonymous);

        await FluentActions.Awaiting(() => MapTileAccessLookup.GetAsync(null!, "https://gw/wms", NullLogger.Instance))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => MapTileAccessLookup.GetAsync(provider, "https://gw/wms", null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TheFallbackProvider_LoadsEveryLayerWithoutAToken()
    {
        var access = await new AnonymousMapTileAccessProvider().GetAccessAsync(new Uri("https://gw/wms"));

        access.Should().Be(MapTileAccess.Anonymous);
        await FluentActions.Awaiting(() => new AnonymousMapTileAccessProvider().GetAccessAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void PanelState_RegistersTheFallback_AndKeepsAHostsOwnProvider()
    {
        var bare = new ServiceCollection();
        bare.AddGeoAssetsPanelState();
        bare.Should().ContainSingle(d => d.ServiceType == typeof(IMapTileAccessProvider))
            .Which.ImplementationType.Should().Be<AnonymousMapTileAccessProvider>();

        var host = new ServiceCollection();
        host.AddScoped<IMapTileAccessProvider>(_ => new FakeProvider(_ => MapTileAccess.Anonymous));
        host.AddGeoAssetsPanelState();
        host.Should().ContainSingle(d => d.ServiceType == typeof(IMapTileAccessProvider))
            .Which.ImplementationFactory.Should().NotBeNull();
    }
}
