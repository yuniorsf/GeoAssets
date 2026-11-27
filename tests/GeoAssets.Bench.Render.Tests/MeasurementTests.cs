using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Bench.Render.Tests;

/// <summary>The simulated client render, the percentiles and the command line.</summary>
public sealed class MeasurementTests
{
    private static readonly BenchAssetType Poste = DatasetComposition.AssetTypes.Single(t => string.Equals(t.Code, "POSTE", StringComparison.Ordinal));
    private static readonly BenchAssetType RedeMt = DatasetComposition.AssetTypes.Single(t => string.Equals(t.Code, "REDE-MT", StringComparison.Ordinal));
    private static readonly BenchAssetType Setor = DatasetComposition.AssetTypes.Single(t => string.Equals(t.Code, "SETOR", StringComparison.Ordinal));

    [Fact]
    public void Client_ParsesEveryFeature_ButDrawsOnlyTheTypesOfTheViewportsZoom()
    {
        var payload = Payload(
            Feature("p", Poste, new GeoPoint(-46.63, -23.55)),
            Feature("l", RedeMt, new GeoLineString([(-46.64, -23.55), (-46.63, -23.551), (-46.62, -23.55)])),
            Feature("s", Setor, new GeoPolygon([(-46.64, -23.56), (-46.62, -23.56), (-46.62, -23.54), (-46.64, -23.56)])),
            Feature("u", null, new GeoPoint(-46.63, -23.55)),
            Feature("n", Poste, null));
        var renderer = new SimulatedClientRenderer(new FakeTimeProvider());

        var z12 = renderer.Render(payload, new MapViewport(-46.63, -23.55, 12, 1580, 1020));
        z12.ParsedFeatures.Should().Be(5);
        z12.DrawnFeatures.Should().Be(2, "at z12 the feeder and the district are drawn, the pole (z15+) is not");
        z12.ProjectedVertices.Should().Be(3 + 4);
        z12.PathCharacters.Should().BePositive();

        var z16 = renderer.Render(payload, new MapViewport(-46.63, -23.55, 16, 1580, 1020));
        z16.DrawnFeatures.Should().Be(2, "at z16 the pole and the feeder are drawn, the district (≤ z13) is not");
        z16.ProjectedVertices.Should().Be(1 + 3);
    }

    [Fact]
    public void Client_ReportsTheElapsedTimeOfItsClock()
    {
        var clock = new SteppingTimeProvider(TimeSpan.FromMilliseconds(40));
        var result = new SimulatedClientRenderer(clock).Render(Payload(), new MapViewport(0, 0, 8, 100, 100));

        result.Elapsed.Should().Be(TimeSpan.FromMilliseconds(40));
        result.ParsedFeatures.Should().Be(0);
    }

    [Fact]
    public void Simplify_DropsCollinearAndSubPixelVertices_KeepsCorners()
    {
        var straight = Enumerable.Range(0, 50).Select(i => ((double)i, 0.0)).ToList();
        SimulatedClientRenderer.Simplify(straight, 1).Should().Equal((0.0, 0.0), (49.0, 0.0));

        var corner = new List<(double, double)> { (0, 0), (10, 0), (10.2, 0.3), (10, 10) };
        SimulatedClientRenderer.Simplify(corner, 1).Should().Equal((0.0, 0.0), (10.0, 0.0), (10.0, 10.0));

        var two = new List<(double, double)> { (0, 0), (0.1, 0.1) };
        SimulatedClientRenderer.Simplify(two, 1).Should().Equal(two);
        SimulatedClientRenderer.Simplify(corner, 0).Should().Equal(corner);
    }

    [Fact]
    public void Percentiles_AreNearestRank()
    {
        var summary = PercentileSummary.Of(Enumerable.Range(1, 100).Select(i => (double)(101 - i)));
        summary.P50.Should().Be(50);
        summary.P95.Should().Be(95);
        summary.Count.Should().Be(100);

        PercentileSummary.Of([7]).Should().Be(new PercentileSummary(7, 7, 1));
        PercentileSummary.Of([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]).P95.Should().Be(12);
        FluentActions.Invoking(() => PercentileSummary.Of([])).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Gzip_ShrinksTheIndentedBody()
    {
        var body = Payload(Enumerable.Range(0, 200).Select(i => Feature($"f{i}", Poste, new GeoPoint(-46.6 + (i * 1e-4), -23.5))).ToArray());

        RenderBenchmark.GzipLength(body).Should().BePositive().And.BeLessThan(body.Length / 4);
    }

    [Fact]
    public void Options_Defaults_AreTheRecordedBaselinesSettings()
    {
        var (options, error) = BenchOptions.Parse(["bench"]);

        error.Should().BeNull();
        options!.Command.Should().Be("bench");
        options.Features.Should().Be(200_000);
        options.OutPath.Should().BeNull();
        options.Settings.Should().BeEquivalentTo(new BenchSettings(420, [8, 12, 16], 12, 5, 1, 1580, 1020));
    }

    [Fact]
    public void Options_ParseEveryValue()
    {
        var (options, error) = BenchOptions.Parse(
            ["seed", "--features", "5000", "--seed", "9", "--zooms", "10, 14", "--viewports", "3", "--iterations", "2", "--warmup", "0", "--viewport", "800x600", "--out", "r.json"]);

        error.Should().BeNull();
        options!.Command.Should().Be("seed");
        options.Features.Should().Be(5_000);
        options.OutPath.Should().Be("r.json");
        options.Settings.Should().BeEquivalentTo(new BenchSettings(9, [10, 14], 3, 2, 0, 800, 600));
    }

    [Theory]
    [InlineData(new string[0], "*'seed', 'bench' or 'tiles'*")]
    [InlineData(new[] { "run" }, "*'seed', 'bench' or 'tiles'*")]
    [InlineData(new[] { "bench", "--iterations" }, "*needs a value*")]
    [InlineData(new[] { "bench", "--iterations", "1", "--iterations", "2" }, "*more than once*")]
    [InlineData(new[] { "bench", "--iterations", "0" }, "*must be positive*")]
    [InlineData(new[] { "bench", "--warmup", "-1" }, "*must not be negative*")]
    [InlineData(new[] { "bench", "--viewport", "800" }, "*<width>x<height>*")]
    [InlineData(new[] { "bench", "--viewport", "0x10" }, "*must be positive*")]
    [InlineData(new[] { "bench", "--zooms", "8,30" }, "*zooms 0–22*")]
    [InlineData(new[] { "bench", "--viewports", "many" }, "*")]
    [InlineData(new[] { "bench", "--seed", "-1" }, "*")]
    [InlineData(new[] { "bench", "--colour", "red" }, "*Unknown option --colour*")]
    public void Options_RejectBadCommandLines(string[] args, string message)
    {
        var (options, error) = BenchOptions.Parse(args);

        options.Should().BeNull();
        error.Should().Match(message);
    }

    private static GeoFeature Feature(string id, BenchAssetType? type, GeoGeometry? geometry) => new()
    {
        Id = id,
        Geometry = geometry,
        Properties = new GeoFeatureProperties { Name = id, AssetTypeId = type?.Id.ToString() ?? Guid.Empty.ToString() },
    };

    private static byte[] Payload(params GeoFeature[] features) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize<IReadOnlyList<GeoFeature>>(features, GeoJsonSerializer.Options));

    /// <summary>Advances by a fixed step on every timestamp read, so an elapsed time is exactly one step.</summary>
    private sealed class SteppingTimeProvider(TimeSpan step) : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            var now = _ticks;
            _ticks += step.Ticks;
            return now;
        }
    }
}
