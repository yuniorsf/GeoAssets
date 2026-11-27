using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Tiles;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-439 — <c>GET /tiles/lod-calibration</c> on a host wired as the kernel wires it, over a scripted density sampler:
/// <c>features:read</c> and <c>organizations:edit</c> both required, the caller's visible asset types calibrated (or those
/// named), the recommendation next to the current thresholds with the settings that would apply it, the calibration
/// settings read from <c>GeoAssets:Tiles:Calibration</c>, a timed-out read answered 504, no catalog warm-up, and nothing
/// applied to the served policy.
/// </summary>
public sealed class TileLodCalibrationEndpointTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000439");
    private static readonly AssetType Poles = new() { Id = Guid.Parse("00000000-0000-0000-0000-0000000004b1"), Name = "Poles", AllowedGeometryType = GeometryType.Point };
    private static readonly AssetType Feeders = new() { Id = Guid.Parse("00000000-0000-0000-0000-0000000004b2"), Name = "Feeders", AllowedGeometryType = GeometryType.LineString, MinZoom = 10 };
    private static readonly string PoleLayer = TileLayerSetDescriptor.LayerIdOf(Poles);
    private static readonly string FeederLayer = TileLayerSetDescriptor.LayerIdOf(Feeders);
    private static readonly Uri Route = new(TileLodCalibrationEndpoint.Route, UriKind.Relative);

    private static async Task<TileTestHost> HostAsync(params (string Key, string Value)[] settings)
    {
        var host = await TileTestHost.StartAsync([Poles, Feeders], settings);

        // Feeders: vector-cheap from z14, raster-cheap at z12–13 (too many features for vector), too dense for either below.
        host.Sampler.Density = (layer, zoom) => !string.Equals(layer, FeederLayer, StringComparison.Ordinal) ? (1, 1)
            : zoom >= 14 ? (50, 5_000)
            : zoom >= 12 ? (2_200, 10_000)
            : (5_000, 900_000);
        return host;
    }

    private static async Task<JsonNode> CalibrateAsync(TileTestHost host, string query = "")
    {
        using var response = await host.ClientFor(OrgA, admin: true).GetAsync(new Uri(Route + query, UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static JsonNode Type(JsonNode body, string layer) =>
        body["assetTypes"]!.AsArray().Single(t => string.Equals(t!["assetTypeId"]!.GetValue<string>(), layer, StringComparison.Ordinal))!;

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ItNeedsBothFeaturesReadAndTheAdminPermission()
    {
        await using var host = await HostAsync();

        using var anonymous = await host.ClientFor(organization: null).GetAsync(Route);
        using var reader = await host.ClientFor(OrgA).GetAsync(Route);
        using var adminWithoutRead = await host.ClientFor(OrgA, withFeaturesRead: false, admin: true).GetAsync(Route);
        using var admin = await host.ClientFor(OrgA, admin: true).GetAsync(Route);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        reader.StatusCode.Should().Be(HttpStatusCode.Forbidden, "features:read alone does not start a calibration");
        adminWithoutRead.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        admin.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Sampler.Measured.Select(m => m.AssetTypeId).Distinct(StringComparer.Ordinal).Should().BeEquivalentTo([PoleLayer, FeederLayer], "only the admin's request reached the sampler");
    }

    // ── What it answers ───────────────────────────────────────────────────────

    [Fact]
    public async Task ItRecommendsPerAssetType_NextToTheCurrentThresholds_WithTheSettingsThatApplyTheChange()
    {
        await using var host = await HostAsync();

        var body = await CalibrateAsync(host);

        body["organizationId"]!.GetValue<Guid>().Should().Be(OrgA);
        body["omittedAssetTypeIds"]!.AsArray().Should().BeEmpty();
        body["settings"]!["maxZoom"]!.GetValue<int>().Should().Be(LodCalibrationSettings.Default.MaxZoom);

        var feeders = Type(body, FeederLayer);
        feeders["name"]!.GetValue<string>().Should().Be("Feeders");
        feeders["seeds"]!.GetValue<int>().Should().Be(1);
        feeders["current"]!["vectorFromZoom"]!.GetValue<int>().Should().Be(0);
        feeders["recommended"]!["rasterFromZoom"]!.GetValue<int>().Should().Be(12);
        feeders["recommended"]!["vectorFromZoom"]!.GetValue<int>().Should().Be(14);
        feeders["changed"]!.GetValue<bool>().Should().BeTrue();
        feeders["zooms"]!.AsArray().Select(z => z!["zoom"]!.GetValue<int>()).Should().Equal(18, 17, 16, 15, 14, 13, 12, 11);
        feeders["zooms"]![0]!["vertices"]!["p95"]!.GetValue<long>().Should().Be(5_000);

        var poles = Type(body, PoleLayer);
        poles["recommended"]!["vectorFromZoom"]!.GetValue<int>().Should().Be(0);
        poles["changed"]!.GetValue<bool>().Should().BeFalse();

        body["configuration"]!.AsObject().Select(p => (p.Key, p.Value!.GetValue<string>())).Should().BeEquivalentTo(
        [
            ($"GeoAssets:Tiles:Lod:Layers:{FeederLayer}:RasterFromZoom", "12"),
            ($"GeoAssets:Tiles:Lod:Layers:{FeederLayer}:VectorFromZoom", "14"),
        ]);
    }

    [Fact]
    public async Task ARecommendationEqualToTheConfiguredThresholds_IsUnchanged_AndNeedsNoSetting()
    {
        await using var host = await HostAsync(
            ($"GeoAssets:Tiles:Lod:Layers:{FeederLayer}:RasterFromZoom", "12"), ($"GeoAssets:Tiles:Lod:Layers:{FeederLayer}:VectorFromZoom", "14"));

        var body = await CalibrateAsync(host);

        Type(body, FeederLayer)["changed"]!.GetValue<bool>().Should().BeFalse();
        body["configuration"]!.AsObject().Should().BeEmpty();
    }

    [Fact]
    public async Task NothingIsApplied_TheServedPolicyStaysAsConfigured()
    {
        await using var host = await HostAsync();

        _ = await CalibrateAsync(host);

        host.Serving.Lod.Decide(FeederLayer, 11).Should().Be(TileRenderTier.Vector, "a recommendation is a configuration change for an operator");
    }

    [Fact]
    public async Task TheNamedAssetTypes_AreTheOnlyOnesCalibrated()
    {
        await using var host = await HostAsync();

        var body = await CalibrateAsync(host, $"?assetTypes={FeederLayer.ToUpperInvariant()}");

        body["assetTypes"]!.AsArray().Select(t => t!["assetTypeId"]!.GetValue<string>()).Should().Equal(FeederLayer);
        host.Sampler.Measured.Should().OnlyContain(m => m.AssetTypeId == FeederLayer);
    }

    [Fact]
    public async Task AnAssetTypeTheCallerDoesNotSee_Is400()
    {
        await using var host = await HostAsync();

        using var response = await host.ClientFor(OrgA, admin: true).GetAsync(new Uri($"{Route}?assetTypes={FeederLayer},nope", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("nope");
        host.Sampler.Measured.Should().BeEmpty();
    }

    [Fact]
    public async Task TheCalibrationSettings_AreTheHostsConfiguration()
    {
        await using var host = await HostAsync(("GeoAssets:Tiles:Calibration:MaxZoom", "15"), ("GeoAssets:Tiles:Calibration:VectorMaxFeaturesPerTile", "3000"));

        var body = await CalibrateAsync(host, $"?assetTypes={FeederLayer}");

        var feeders = Type(body, FeederLayer);
        feeders["zooms"]![0]!["zoom"]!.GetValue<int>().Should().Be(15);
        feeders["recommended"]!["vectorFromZoom"]!.GetValue<int>().Should().Be(12, "2,200 features now fit the vector budget");
        body["settings"]!["vectorMaxFeaturesPerTile"]!.GetValue<int>().Should().Be(3_000);
    }

    [Fact]
    public async Task AReadPastItsTimeout_Is504()
    {
        await using var host = await HostAsync();
        host.Sampler.Failure = new TimeoutException("A LOD calibration statement ran past its 30 s timeout.");

        using var response = await host.ClientFor(OrgA, admin: true).GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        (await response.Content.ReadAsStringAsync()).Should().Contain("assetTypes=");
    }

    [Fact]
    public async Task ItNeverLoadsTheOrganizationCatalog()
    {
        await using var host = await HostAsync();

        _ = await CalibrateAsync(host);

        host.Catalogs.Warmed.Should().Be(0);
    }
}
