using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests.FeatureCatalogs;

/// <summary>
/// XD01-478: WFS GetFeature with BBOX is one page of the catalog's store, like without BBOX (XD01-449) — the box goes
/// into the store's query (<see cref="AssetQuery.Bounds"/>) with STARTINDEX/COUNT, at most
/// <see cref="WfsEndpointExtensions.MaxFeaturesPerPage"/> features, <c>numberMatched</c> exact up to
/// <see cref="WfsEndpointExtensions.MaxNumberMatched"/> and <c>"unknown"</c> beyond, and the request's abort token handed
/// to the read — never every feature in the box materialized before COUNT.
/// </summary>
public sealed class WfsGetFeatureBboxTests : IAsyncLifetime
{
    private const string Pole = "pole";
    private const string Cable = "cable";
    private const string Box = "&BBOX=-39,-13,-38,-12,urn:ogc:def:crs:EPSG::4326";

    private readonly RecordingAssetProvider _catalog = new();
    private IHost? _host;
    private HttpClient _client = null!;

    /// <summary>
    /// The in-memory catalog, its cancellable page read recorded — and, when <see cref="Reported"/> is set, answered with it,
    /// or held until cancelled when <see cref="HoldUntilCancelled"/> is.
    /// </summary>
    private sealed class RecordingAssetProvider : TestAssetProvider, IAssetProvider
    {
        public List<AssetQuery> Queries { get; } = [];

        public PagedResult<GeoFeature>? Reported { get; set; }

        public bool HoldUntilCancelled { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PagedResult<GeoFeature>> GetPageAsync(AssetQuery query, CancellationToken ct)
        {
            Queries.Add(query);
            if (HoldUntilCancelled)
            {
                await using var registration = ct.Register(() => Cancelled.TrySetResult());
                Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, ct);
            }

            return Reported ?? await InMemoryPageAsync(this, query);
        }
    }

    /// <summary><see cref="IAssetProvider"/>'s in-memory default over the seeded features.</summary>
    private static Task<PagedResult<GeoFeature>> InMemoryPageAsync(IAssetProvider provider, AssetQuery query) => provider.GetPageAsync(query);

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthorization(options => options.AddPolicy("features:read", policy => policy.RequireAssertion(_ => true)));
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<IAssetProvider>(_catalog);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapWfsApi());
                });
            })
            .StartAsync();
        _client = _host.GetTestServer().CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private void Seed(int count, string assetType, string prefix, double lon = -38.5, double lat = -12.5) =>
        _catalog.AddRange(Enumerable.Range(0, count).Select(i => new GeoFeature
        {
            Id = $"{prefix}-{i:D6}",
            Geometry = new GeoPoint(lon, lat),
            Properties = new GeoFeatureProperties { Name = $"{prefix} {i}", AssetTypeId = assetType },
        }));

    private static Uri FeatureUri(string query) => new($"/wfs?SERVICE=WFS&REQUEST=GetFeature{query}", UriKind.Relative);

    private async Task<(JsonElement Matched, int Returned, List<string> Ids)> GetFeatureAsync(string query)
    {
        using var response = await _client.GetAsync(FeatureUri(query));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var ids = root.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetString()!).ToList();
        return (root.GetProperty("numberMatched").Clone(), root.GetProperty("numberReturned").GetInt32(), ids);
    }

    [Fact]
    public async Task ABoxWithoutCount_ReturnsAtMostThePageCap_AndTheExactNumberMatched_OfTheBoxOnly()
    {
        Seed(10_005, Pole, "p");
        Seed(3, Pole, "far", lon: 10, lat: 10);

        var (matched, returned, ids) = await GetFeatureAsync(Box);

        matched.GetInt32().Should().Be(10_005);
        returned.Should().Be(WfsEndpointExtensions.MaxFeaturesPerPage);
        ids.Should().HaveCount(WfsEndpointExtensions.MaxFeaturesPerPage).And.NotContain(id => id.StartsWith("far", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABoxWithCountAndStartIndex_SelectsOnePage_InIdOrder()
    {
        Seed(7, Pole, "p");
        Seed(2, Pole, "far", lon: 10, lat: 10);

        var (matched, returned, ids) = await GetFeatureAsync($"&COUNT=3&STARTINDEX=2{Box}");

        matched.GetInt32().Should().Be(7);
        returned.Should().Be(3);
        ids.Should().Equal("p-000002", "p-000003", "p-000004");
    }

    [Fact]
    public async Task ABoxWithACountAboveThePageCap_IsCapped()
    {
        Seed(10_002, Pole, "p");

        var (_, returned, _) = await GetFeatureAsync($"&COUNT=50000{Box}");

        returned.Should().Be(WfsEndpointExtensions.MaxFeaturesPerPage);
    }

    [Fact]
    public async Task ABoxWithAFeatureTypeName_FiltersByItsAssetType()
    {
        Seed(4, Pole, "p");
        Seed(2, Cable, "c");

        var (matched, returned, ids) = await GetFeatureAsync($"&TYPENAMES=geoassets:feature:{Cable}{Box}");

        matched.GetInt32().Should().Be(2);
        returned.Should().Be(2);
        ids.Should().Equal("c-000000", "c-000001");
    }

    [Fact]
    public async Task ABox_ReachesTheStoreAsItsQuery_WithTheCountBounded()
    {
        Seed(1, Pole, "p");

        await GetFeatureAsync($"&COUNT=20&STARTINDEX=5{Box}");

        var query = _catalog.Queries.Should().ContainSingle().Subject;
        query.Bounds.Should().Be(new BoundingBox(-39, -13, -38, -12));
        query.CountLimit.Should().Be(WfsEndpointExtensions.MaxNumberMatched);
        query.Skip.Should().Be(5);
        query.Take.Should().Be(20);
    }

    [Fact]
    public async Task WithoutABox_TheStoresCountIsBoundedToo()
    {
        Seed(1, Pole, "p");

        await GetFeatureAsync(string.Empty);

        var query = _catalog.Queries.Should().ContainSingle().Subject;
        query.Bounds.Should().BeNull();
        query.CountLimit.Should().Be(WfsEndpointExtensions.MaxNumberMatched);
    }

    [Fact]
    public async Task WithoutABox_ACatalogAboveTheCountLimit_ReportsNumberMatchedAsUnknown()
    {
        _catalog.Reported = new PagedResult<GeoFeature> { Items = [], TotalCount = WfsEndpointExtensions.MaxNumberMatched + 1 };

        var (matched, returned, _) = await GetFeatureAsync("&COUNT=0");

        matched.ValueKind.Should().Be(JsonValueKind.String);
        matched.GetString().Should().Be("unknown");
        returned.Should().Be(0);
    }

    [Fact]
    public async Task WithoutABox_ACatalogOfExactlyTheCountLimit_ReportsItsNumber()
    {
        _catalog.Reported = new PagedResult<GeoFeature> { Items = [], TotalCount = WfsEndpointExtensions.MaxNumberMatched };

        var (matched, _, _) = await GetFeatureAsync("&COUNT=0");

        matched.GetInt32().Should().Be(WfsEndpointExtensions.MaxNumberMatched);
    }

    [Fact]
    public async Task ABoxMatchingMoreThanTheCountLimit_ReportsNumberMatchedAsUnknown()
    {
        _catalog.Reported = new PagedResult<GeoFeature> { Items = [], TotalCount = WfsEndpointExtensions.MaxNumberMatched + 1 };

        var (matched, returned, _) = await GetFeatureAsync($"&COUNT=0{Box}");

        matched.ValueKind.Should().Be(JsonValueKind.String);
        matched.GetString().Should().Be("unknown");
        returned.Should().Be(0);
    }

    [Fact]
    public async Task ABoxMatchingExactlyTheCountLimit_ReportsItsNumber()
    {
        _catalog.Reported = new PagedResult<GeoFeature> { Items = [], TotalCount = WfsEndpointExtensions.MaxNumberMatched };

        var (matched, _, _) = await GetFeatureAsync($"&COUNT=0{Box}");

        matched.GetInt32().Should().Be(WfsEndpointExtensions.MaxNumberMatched);
    }

    [Theory]
    [InlineData("&BBOX=-39,-13,-38")]
    [InlineData("&BBOX=a,-13,-38,-12")]
    public async Task AnInvalidBox_IsABadRequest_AndReadsNothing(string box)
    {
        using var response = await _client.GetAsync(FeatureUri(box));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _catalog.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task AClientThatGoesAway_CancelsTheStoresRead()
    {
        _catalog.HoldUntilCancelled = true;
        using var abandon = new CancellationTokenSource();

        var request = _client.GetAsync(FeatureUri(Box), abandon.Token);
        await _catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await abandon.CancelAsync();

        await _catalog.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var act = () => request;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
