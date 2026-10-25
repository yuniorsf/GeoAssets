using System.IO.Compression;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace GeoAssets.Provider.Shapefile.Tests;

/// <summary>
/// Covers <see cref="ShapefileExportService"/> — the write-side mirror of
/// <see cref="ShapefileParser"/>, exercised through its only public entry point,
/// <see cref="IShapefileExportService.ExportToZip"/>. Written zips are unzipped into a temp dir
/// and read back via <see cref="ShapefileDataReader"/> (the same NTS reader
/// <c>ShapefileParser</c> uses) to verify both geometry and attribute round-tripping.
/// </summary>
public class ShapefileExportServiceTests
{
    private static GeoFeature PointFeature(string name, double lon, double lat, Dictionary<string, string>? attrs = null) => new()
    {
        Geometry = new GeoPoint(lon, lat),
        Properties = new GeoFeatureProperties { Name = name, CustomAttributes = attrs ?? [] },
    };

    private static GeoFeature LineFeature(string name) => new()
    {
        Geometry = new GeoLineString([(0, 0), (1, 1)]),
        Properties = new GeoFeatureProperties { Name = name },
    };

    private static GeoFeature PolygonFeature(string name) => new()
    {
        Geometry = new GeoPolygon([(0, 0), (0, 1), (1, 1), (1, 0), (0, 0)]),
        Properties = new GeoFeatureProperties { Name = name },
    };

    /// <summary>Unzips <paramref name="zip"/> into a fresh temp dir and returns it — caller must delete.</summary>
    private static string Unzip(byte[] zip)
    {
        var dir = Path.Combine(Path.GetTempPath(), "shp_export_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
            entry.ExtractToFile(Path.Combine(dir, entry.Name));
        return dir;
    }

    [Fact]
    public void ExportToZip_NoFeatures_Throws()
    {
        var sut = new ShapefileExportService();

        var act = () => sut.ExportToZip([]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ExportToZip_OnlyRawGeometryCollectionFeatures_Throws()
    {
        var sut = new ShapefileExportService();
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var feature = new GeoFeature
        {
            Geometry = GeoGeometry.FromNts(factory.CreateGeometryCollection([factory.CreatePoint(new Coordinate(0, 0))])),
            Properties = new GeoFeatureProperties { Name = "unexportable" },
        };

        var act = () => sut.ExportToZip([feature]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ExportToZip_OnlyPointFeatures_WritesOnlyPointsShapefile()
    {
        var sut = new ShapefileExportService();
        var features = new List<GeoFeature> { PointFeature("A", 1, 2), PointFeature("B", 3, 4) };

        var zip = sut.ExportToZip(features);
        var dir = Unzip(zip);
        try
        {
            File.Exists(Path.Combine(dir, "points.shp")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "points.prj")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "lines.shp")).Should().BeFalse();
            File.Exists(Path.Combine(dir, "polygons.shp")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ExportToZip_MixedGeometryTypes_WritesOneShapefilePerCategory()
    {
        var sut = new ShapefileExportService();
        var features = new List<GeoFeature> { PointFeature("P", 0, 0), LineFeature("L"), PolygonFeature("Poly") };

        var zip = sut.ExportToZip(features);
        var dir = Unzip(zip);
        try
        {
            File.Exists(Path.Combine(dir, "points.shp")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "lines.shp")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "polygons.shp")).Should().BeTrue();

            ReadNames(Path.Combine(dir, "points")).Should().Equal("P");
            ReadNames(Path.Combine(dir, "lines")).Should().Equal("L");
            ReadNames(Path.Combine(dir, "polygons")).Should().Equal("Poly");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ExportToZip_FeatureWithCustomAttributes_RoundTripsNameAndAttributes()
    {
        var sut = new ShapefileExportService();
        var feature = PointFeature("Hydrant 1", 10, 20, new Dictionary<string, string>(StringComparer.Ordinal) { ["material"] = "cast-iron", ["year"] = "1998" });

        var zip = sut.ExportToZip([feature]);
        var dir = Unzip(zip);
        try
        {
            var factory = new GeometryFactory(new PrecisionModel(), 4326);
            using var reader = new ShapefileDataReader(Path.Combine(dir, "points"), factory);
            var header = reader.DbaseHeader;
            var fieldNames = Enumerable.Range(0, header.NumFields).Select(i => header.Fields[i].Name).ToList();

            fieldNames.Should().Contain(["NAME", "MATERIAL", "YEAR"]);

            reader.Read().Should().BeTrue();
            reader.Geometry.Should().NotBeNull();
            reader.GetValue(fieldNames.IndexOf("NAME") + 1)!.ToString()!.Trim().Should().Be("Hydrant 1");
            reader.GetValue(fieldNames.IndexOf("MATERIAL") + 1)!.ToString()!.Trim().Should().Be("cast-iron");
            reader.GetValue(fieldNames.IndexOf("YEAR") + 1)!.ToString()!.Trim().Should().Be("1998");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ExportToZip_FeaturesWithDifferentAttributeKeys_EveryRecordGetsEveryColumn()
    {
        // ShapefileDataWriter requires every record to supply a value for every header column —
        // a feature missing a key another feature in the same bucket has must still produce a
        // (blank) value for it rather than throwing.
        var sut = new ShapefileExportService();
        var features = new List<GeoFeature>
        {
            PointFeature("A", 0, 0, new Dictionary<string, string>(StringComparer.Ordinal) { ["foo"] = "1" }),
            PointFeature("B", 1, 1, new Dictionary<string, string>(StringComparer.Ordinal) { ["bar"] = "2" }),
        };

        var act = () => sut.ExportToZip(features);

        act.Should().NotThrow();
    }

    [Fact]
    public void ExportToZip_FeatureWithNullGeometry_IsSkippedNotThrown()
    {
        var sut = new ShapefileExportService();
        var features = new List<GeoFeature>
        {
            new() { Geometry = null, Properties = new GeoFeatureProperties { Name = "no-geom" } },
            PointFeature("ok", 0, 0),
        };

        var zip = sut.ExportToZip(features);

        zip.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(new[] { "short" }, new[] { "SHORT" })]
    [InlineData(new[] { "ThisIsAVeryLongAttributeName" }, new[] { "THISISAVER" })]
    [InlineData(new[] { "name" }, new[] { "NAME1" })] // collides case-insensitively with the reserved NAME column
    public void BuildColumnNameMap_SanitizesAndTruncatesToTenChars(string[] keys, string[] expectedValues)
    {
        var map = ShapefileExportService.BuildColumnNameMap(keys);

        map.Values.Should().BeEquivalentTo(expectedValues);
        map.Values.Should().OnlyContain(v => v.Length <= 10);
    }

    [Fact]
    public void BuildColumnNameMap_KeysThatTruncateToTheSameName_GetDistinctSuffixes()
    {
        var map = ShapefileExportService.BuildColumnNameMap(["MaintenanceDate", "MaintenanceCrew"]);

        map.Values.Should().OnlyHaveUniqueItems();
        map.Values.Should().OnlyContain(v => v.Length <= 10);
    }

    private static List<string> ReadNames(string basePath)
    {
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        using var reader = new ShapefileDataReader(basePath, factory);
        var header = reader.DbaseHeader;
        var nameIndex = Enumerable.Range(0, header.NumFields).First(i => string.Equals(header.Fields[i].Name, "NAME", StringComparison.Ordinal));

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetValue(nameIndex + 1)?.ToString()?.Trim() ?? string.Empty);
        return names;
    }
}
