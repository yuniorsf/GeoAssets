using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests.FeatureCatalogs;

/// <summary>
/// XD01-479: <c>POST /api/geoassets/features/query</c> is one bounded page — <c>take</c> clamped to
/// <see cref="GeoAssetsRestApiExtensions.MaxQueryTake"/>, <c>countLimit</c> clamped to (and defaulted to)
/// <see cref="GeoAssetsRestApiExtensions.MaxQueryCountLimit"/>, a total beyond it reported as the limit + 1 with
/// <c>totalCountCapped</c> — and the request's abort token reaches the store's read.
/// </summary>
public sealed class FeaturesQueryBoundsTests : IAsyncLifetime
{
    private static readonly Uri QueryUri = new("/api/geoassets/features/query", UriKind.Relative);

    private readonly RecordingAssetProvider _catalog = new();
    private IHost? _host;
    private HttpClient _client = null!;

    /// <summary>
    /// The in-memory catalog, its cancellable page read recorded — answered with <see cref="Reported"/> when set, or held
    /// until cancelled when <see cref="HoldUntilCancelled"/> is.
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
                    services.AddScoped<IAssetRenderQuery>(sp => new AssetProviderRenderQuery(sp.GetRequiredService<IAssetProvider>()));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGeoAssetsApi());
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

    private void Seed(int count) =>
        _catalog.AddRange(Enumerable.Range(0, count).Select(i => new GeoFeature
        {
            Id = $"p-{i:D6}",
            Properties = new GeoFeatureProperties { Name = $"p {i}", AssetTypeId = "pole" },
        }));

    private async Task<(PagedResult<GeoFeature> Page, JsonElement Body)> QueryAsync(string body)
    {
        using var response = await _client.PostAsync(QueryUri, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return (JsonSerializer.Deserialize<PagedResult<GeoFeature>>(json, GeoJsonSerializer.Options)!, document.RootElement.Clone());
    }

    [Fact]
    public void TheCaps_AreTheWfsPageCapAndCountBound()
    {
        GeoAssetsRestApiExtensions.MaxQueryTake.Should().Be(WfsEndpointExtensions.MaxFeaturesPerPage).And.Be(10_000);
        GeoAssetsRestApiExtensions.MaxQueryCountLimit.Should().Be(WfsEndpointExtensions.MaxNumberMatched).And.Be(100_000);
    }

    [Fact]
    public async Task ATakeAboveTheMaximum_ReturnsAtMostTheMaximum()
    {
        Seed(GeoAssetsRestApiExtensions.MaxQueryTake + 5);

        var (page, _) = await QueryAsync("""{"take":5000000}""");

        page.Items.Should().HaveCount(GeoAssetsRestApiExtensions.MaxQueryTake);
        page.TotalCount.Should().Be(GeoAssetsRestApiExtensions.MaxQueryTake + 5, "a total under the count limit is exact");
        page.TotalCountCapped.Should().BeFalse();
        _catalog.Queries.Should().ContainSingle().Which.Take.Should().Be(GeoAssetsRestApiExtensions.MaxQueryTake);
    }

    [Fact]
    public async Task ATakeAndSkipWithinBounds_PassThrough_AndANegativeOneIsZero()
    {
        Seed(10);

        var (page, _) = await QueryAsync("""{"skip":2,"take":3}""");
        await QueryAsync("""{"skip":-4,"take":-1}""");

        page.Items.Select(f => f.Id).Should().Equal("p-000002", "p-000003", "p-000004");
        _catalog.Queries[0].Should().Match<AssetQuery>(q => q.Skip == 2 && q.Take == 3);
        _catalog.Queries[1].Should().Match<AssetQuery>(q => q.Skip == 0 && q.Take == 0);
    }

    [Theory]
    [InlineData("""{"take":5}""", GeoAssetsRestApiExtensions.MaxQueryCountLimit)]
    [InlineData("""{"take":5,"countLimit":null}""", GeoAssetsRestApiExtensions.MaxQueryCountLimit)]
    [InlineData("""{"take":5,"countLimit":2147483647}""", GeoAssetsRestApiExtensions.MaxQueryCountLimit)]
    [InlineData("""{"take":5,"countLimit":250}""", 250)]
    [InlineData("""{"take":5,"countLimit":-3}""", 0)]
    public async Task AnAbsentOrOversizedCountLimit_IsClamped(string body, int expectedCountLimit)
    {
        Seed(1);

        await QueryAsync(body);

        _catalog.Queries.Should().ContainSingle().Which.CountLimit.Should().Be(expectedCountLimit);
    }

    [Fact]
    public async Task MoreMatchesThanTheCountLimit_ReportTheLimitPlusOne_AsCapped()
    {
        _catalog.Reported = new PagedResult<GeoFeature> { Items = [], TotalCount = GeoAssetsRestApiExtensions.MaxQueryCountLimit + 1 };

        var (page, body) = await QueryAsync("""{"take":5}""");

        page.TotalCount.Should().Be(GeoAssetsRestApiExtensions.MaxQueryCountLimit + 1);
        page.TotalCountCapped.Should().BeTrue();
        body.GetProperty("totalCountCapped").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AStoreThatCountsPastTheLimit_IsStillReportedAsTheLimitPlusOne()
    {
        _catalog.Reported = new PagedResult<GeoFeature> { Items = [], TotalCount = 4_169_154 };

        var (page, _) = await QueryAsync("""{"take":5,"countLimit":1000}""");

        page.TotalCount.Should().Be(1_001);
        page.TotalCountCapped.Should().BeTrue();
    }

    [Fact]
    public async Task ExactlyTheCountLimitMatching_IsAnExactTotal_WithoutTheFlag()
    {
        _catalog.Reported = new PagedResult<GeoFeature> { Items = [], TotalCount = 7 };

        var (page, body) = await QueryAsync("""{"take":5,"countLimit":7}""");

        page.TotalCount.Should().Be(7);
        page.TotalCountCapped.Should().BeFalse();
        body.TryGetProperty("totalCountCapped", out _).Should().BeFalse("an exact total keeps the page's body as before");
    }

    [Fact]
    public async Task ARequestThatGoesAway_CancelsTheStoresRead()
    {
        _catalog.HoldUntilCancelled = true;
        using var abandon = new CancellationTokenSource();

        var request = _client.PostAsJsonAsync(QueryUri, new AssetQuery { Take = 5 }, GeoJsonSerializer.Options, abandon.Token);
        await _catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
        await abandon.CancelAsync();

        var act = () => request;
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _catalog.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
    }
}
