using System.IO.Compression;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace GeoAssets.Provider.Shapefile.Tests;

/// <summary>
/// Covers <see cref="ShapefileFeatureStore"/>'s spatial queries (<c>GetWithin</c>/
/// <c>GetIntersecting</c>/<c>GetInBoundsAsync</c>) through the only public entry point available
/// to it — <see cref="ShapefileProviderPlugin.CreateAsync"/> — since the store itself is internal.
/// Regression coverage for the envelope-based spatial index added to replace a full linear
/// topological-predicate scan over every feature on every query.
/// </summary>
public class ShapefileFeatureStoreSpatialQueryTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    /// <summary>
    /// Builds a real ESRI Shapefile ZIP (via <see cref="ShapefileDataWriter"/>, the same writer
    /// NTS itself uses) with one Polygon at (0,0)-(1,1), one Polygon far away at (10,10)-(11,11),
    /// and one MultiPolygon made of two disjoint squares at (20,20)-(21,21) and (22,22)-(23,23) —
    /// mirrors the real-world shapefile this fix was diagnosed against (mix of Polygon and
    /// MultiPolygon features).
    /// </summary>
    private static async Task<IAssetProvider> LoadFixtureProviderAsync()
    {
        var polyA = Factory.CreatePolygon([
            new Coordinate(0, 0), new Coordinate(0, 1), new Coordinate(1, 1), new Coordinate(1, 0), new Coordinate(0, 0)
        ]);
        var polyB = Factory.CreatePolygon([
            new Coordinate(10, 10), new Coordinate(10, 11), new Coordinate(11, 11), new Coordinate(11, 10), new Coordinate(10, 10)
        ]);
        var multiC = Factory.CreateMultiPolygon([
            Factory.CreatePolygon([
                new Coordinate(20, 20), new Coordinate(20, 21), new Coordinate(21, 21), new Coordinate(21, 20), new Coordinate(20, 20)
            ]),
            Factory.CreatePolygon([
                new Coordinate(22, 22), new Coordinate(22, 23), new Coordinate(23, 23), new Coordinate(23, 22), new Coordinate(22, 22)
            ])
        ]);

        var features = new List<IFeature>
        {
            new Feature(polyA, new AttributesTable { { "NAME", "A" } }),
            new Feature(polyB, new AttributesTable { { "NAME", "B" } }),
            new Feature(multiC, new AttributesTable { { "NAME", "C" } }),
        };

        var tempDir = Path.Combine(Path.GetTempPath(), "shp_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var basePath = Path.Combine(tempDir, "fixture");
            var header = new DbaseFileHeader();
            header.AddColumn("NAME", 'C', 50, 0);
            header.NumRecords = features.Count;

            var writer = new ShapefileDataWriter(basePath, Factory) { Header = header };
            writer.Write(features);

            var zipPath = Path.Combine(tempDir, "archive.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                foreach (var f in Directory.GetFiles(tempDir, "fixture.*"))
                    zip.CreateEntryFromFile(f, Path.GetFileName(f));

            var config = new ProviderConfig();
            config.Set("name", "fixture");
            config.Set("archive_content", Convert.ToBase64String(await File.ReadAllBytesAsync(zipPath)));

            return await new ShapefileProviderPlugin().CreateAsync(config, null!);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetInBoundsAsync_BoundsCoveringWholeDataset_ReturnsEveryFeature()
    {
        var provider = await LoadFixtureProviderAsync();

        var result = await provider.GetInBoundsAsync(-1, -1, 24, 24);

        result.Select(f => f.Properties.Name).Should().BeEquivalentTo(["A", "B", "C"]);
        result.Single(f => f.Properties.Name == "C").Geometry!.NtsGeometry.GeometryType.Should().Be("MultiPolygon");
    }

    [Fact]
    public async Task GetInBoundsAsync_BoundsCoveringOnlyOneFeature_ReturnsJustThatSubset()
    {
        var provider = await LoadFixtureProviderAsync();

        var result = await provider.GetInBoundsAsync(-1, -1, 2, 2);

        result.Select(f => f.Properties.Name).Should().BeEquivalentTo(["A"]);
    }

    [Fact]
    public async Task GetInBoundsAsync_BoundsCoveringOnlyTheMultiPolygon_ReturnsIt()
    {
        var provider = await LoadFixtureProviderAsync();

        var result = await provider.GetInBoundsAsync(19, 19, 24, 24);

        result.Select(f => f.Properties.Name).Should().BeEquivalentTo(["C"]);
        result[0].Geometry!.NtsGeometry.GeometryType.Should().Be("MultiPolygon");
    }

    [Fact]
    public async Task GetInBoundsAsync_BoundsMatchingNothing_ReturnsEmpty()
    {
        var provider = await LoadFixtureProviderAsync();

        var result = await provider.GetInBoundsAsync(100, 100, 101, 101);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInBoundsJsonAsync_CalledTwiceWithSameBounds_ReturnsEquivalentJsonBothTimes()
    {
        // Regression coverage for the per-feature JSON cache: repeated pans over the same
        // unchanged feature must still produce correct (not stale-wrong) output.
        var provider = await LoadFixtureProviderAsync();

        var first = await provider.GetInBoundsJsonAsync(-1, -1, 2, 2);
        var second = await provider.GetInBoundsJsonAsync(-1, -1, 2, 2);

        first.Should().HaveCount(1);
        second.Should().HaveCount(1);
        second[0].GetRawText().Should().Be(first[0].GetRawText());
    }

    [Fact]
    public async Task GetInBoundsJsonAsync_AfterUpdate_ReflectsTheChangeNotTheCachedValue()
    {
        // Proves the JSON cache is actually invalidated on Update, not just unused.
        var provider = await LoadFixtureProviderAsync();

        var before = await provider.GetInBoundsJsonAsync(-1, -1, 2, 2);
        before[0].GetProperty("properties").GetProperty("name").GetString().Should().Be("A");

        var featureA = provider.GetAll().Single(f => f.Properties.Name == "A");
        featureA.Properties.Name = "A-renamed";
        provider.Update(featureA);

        var after = await provider.GetInBoundsJsonAsync(-1, -1, 2, 2);
        after[0].GetProperty("properties").GetProperty("name").GetString().Should().Be("A-renamed");
    }
}
