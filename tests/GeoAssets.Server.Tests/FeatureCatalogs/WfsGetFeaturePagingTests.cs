using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests.FeatureCatalogs;

/// <summary>
/// XD01-449: WFS GetFeature without BBOX is one page of the catalog's store (<see cref="IAssetProvider.GetPageAsync"/>) —
/// at most <see cref="WfsEndpointExtensions.MaxFeaturesPerPage"/> features, STARTINDEX/COUNT applied, NumberMatched the
/// exact count up to <see cref="WfsEndpointExtensions.MaxNumberMatched"/> (XD01-480) — never the whole catalog enumerated
/// in memory.
/// </summary>
public sealed class WfsGetFeaturePagingTests : IAsyncLifetime
{
    private const string Pole = "pole";
    private const string Cable = "cable";

    private readonly TestAssetProvider _catalog = new();
    private IHost? _host;
    private HttpClient _client = null!;

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

    private void Seed(int count, string assetType, string prefix) =>
        _catalog.AddRange(Enumerable.Range(0, count).Select(i => new GeoFeature
        {
            Id = $"{prefix}-{i:D6}",
            Geometry = new GeoPoint(-38.5, -12.9),
            Properties = new GeoFeatureProperties { Name = $"{prefix} {i}", AssetTypeId = assetType },
        }));

    private async Task<(int Matched, int Returned, List<string> Ids)> GetFeatureAsync(string query)
    {
        using var response = await _client.GetAsync(new Uri($"/wfs?SERVICE=WFS&REQUEST=GetFeature{query}", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var ids = root.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetString()!).ToList();
        return (root.GetProperty("numberMatched").GetInt32(), root.GetProperty("numberReturned").GetInt32(), ids);
    }

    [Fact]
    public void ThePageCap_IsTenThousand() => WfsEndpointExtensions.MaxFeaturesPerPage.Should().Be(10_000);

    [Fact]
    public void TheCountLimit_IsAHundredThousand() => WfsEndpointExtensions.MaxNumberMatched.Should().Be(100_000);

    [Fact]
    public async Task WithoutCount_ReturnsAtMostThePageCap_AndTheExactNumberMatched()
    {
        Seed(10_005, Pole, "p");

        var (matched, returned, ids) = await GetFeatureAsync(string.Empty);

        matched.Should().Be(10_005);
        returned.Should().Be(WfsEndpointExtensions.MaxFeaturesPerPage);
        ids.Should().HaveCount(WfsEndpointExtensions.MaxFeaturesPerPage);
    }

    [Fact]
    public async Task CountAndStartIndex_SelectOnePage_InIdOrder()
    {
        Seed(7, Pole, "p");

        var (matched, returned, ids) = await GetFeatureAsync("&COUNT=3&STARTINDEX=2");

        matched.Should().Be(7);
        returned.Should().Be(3);
        ids.Should().Equal("p-000002", "p-000003", "p-000004");
    }

    [Fact]
    public async Task ACountAboveThePageCap_IsCapped()
    {
        Seed(10_002, Pole, "p");

        var (_, returned, _) = await GetFeatureAsync("&COUNT=50000");

        returned.Should().Be(WfsEndpointExtensions.MaxFeaturesPerPage);
    }

    [Fact]
    public async Task AFeatureTypeName_FiltersByItsAssetType()
    {
        Seed(4, Pole, "p");
        Seed(2, Cable, "c");

        var (matched, returned, ids) = await GetFeatureAsync($"&TYPENAMES=geoassets:feature:{Cable}");

        matched.Should().Be(2);
        returned.Should().Be(2);
        ids.Should().Equal("c-000000", "c-000001");
    }
}
