using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Tests.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-439 (ADR-002 D7) — the real kernel <c>Program</c> with the <c>postgres-spatial</c> module, on the route-inventory
/// PostGIS: an invalid <c>GeoAssets:Tiles</c> LOD configuration stops the host while its services are registered — the
/// host is never built, so it never serves a request — and a valid one is the policy the tiles are cut by.
/// </summary>
public sealed class TileLodStartupValidationTests
{
    private const string Feeders = "42000000-0000-0000-0000-000000000003";

    [Theory]
    [InlineData("GeoAssets:Tiles:Lod:RasterFromZoom", "14", "GeoAssets:Tiles:Lod:VectorFromZoom", "10", nameof(LodThresholdsDefect.RasterFromZoomAboveVectorFromZoom))]
    [InlineData("GeoAssets:Tiles:Lod:VectorFromZoom", "25", "GeoAssets:Tiles:Lod:RasterFromZoom", "0", nameof(LodThresholdsDefect.VectorFromZoomOutOfBounds))]
    [InlineData("GeoAssets:Tiles:Lod:Layers:" + Feeders + ":RasterFromZoom", "16", "GeoAssets:Tiles:Lod:Layers:" + Feeders + ":VectorFromZoom", "12", "layer '" + Feeders + "'")]
    [InlineData("GeoAssets:Tiles:Lod:Layers:" + Feeders + ":MinZoom", "-1", "GeoAssets:Tiles:Lod:Layers:" + Feeders + ":VectorFromZoom", "12", nameof(LodThresholdsDefect.MinZoomOutOfBounds))]
    [InlineData("GeoAssets:Tiles:Lod:VectorFromZom", "12", "GeoAssets:Tiles:Lod:RasterFromZoom", "0", "'GeoAssets:Tiles:Lod:VectorFromZom' is not a setting")]
    [InlineData("GeoAssets:Tiles:Lod:Layers:feeders:VectorFromZoom", "12", "GeoAssets:Tiles:Lod:RasterFromZoom", "0", "must be named by an asset type id")]
    [InlineData("GeoAssets:Tiles:Calibration:MinZoom", "12", "GeoAssets:Tiles:Calibration:MaxZoom", "8", "Calibration")]
    public async Task AnInvalidLodConfiguration_StopsTheKernelBeforeItServes(string key, string value, string otherKey, string otherValue, string named)
    {
        await using var unstarted = await ServerHostFixture.CreateUnstartedFactoryAsync("postgres-spatial");
        await using var factory = unstarted.WithWebHostBuilder(host =>
        {
            host.UseSetting(key, value);
            host.UseSetting(otherKey, otherValue);
        });

        var start = () => factory.Services;

        var failure = start.Should().Throw<Exception>().Which;
        Flatten(failure).Should().Contain(e => (e is InvalidLodPolicyException || e is InvalidOperationException) && e.Message.Contains(named, StringComparison.Ordinal));
        var serve = () => factory.CreateClient();
        serve.Should().Throw<Exception>("a host that failed to start has no server to send a request to");
    }

    [Fact]
    public async Task AValidCalibratedConfiguration_IsThePolicyTheTilesAreCutBy()
    {
        await using var unstarted = await ServerHostFixture.CreateUnstartedFactoryAsync("postgres-spatial");
        await using var factory = unstarted.WithWebHostBuilder(host =>
        {
            host.UseSetting($"GeoAssets:Tiles:Lod:Layers:{Feeders}:RasterFromZoom", "13");
            host.UseSetting($"GeoAssets:Tiles:Lod:Layers:{Feeders}:VectorFromZoom", "14");
        });

        var policy = factory.Services.GetRequiredService<ILodPolicy>();

        policy.Decide(Feeders, 12).Should().Be(TileRenderTier.Aggregate);
        policy.Decide(Feeders, 13).Should().Be(TileRenderTier.Raster);
        policy.Decide(Feeders, 14).Should().Be(TileRenderTier.Vector);
        policy.Decide("00000000-0000-0000-0000-000000000001", 12).Should().Be(TileRenderTier.Vector);
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                    yield return inner;
            }
        }
    }
}
