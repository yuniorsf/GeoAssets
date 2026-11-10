using System.IO.Compression;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using MaxRev.Gdal.Core;
using OSGeo.OGR;
using Xunit;

namespace GeoAssets.Provider.FileGeodatabase.Tests;

/// <summary>
/// Covers <see cref="FileGeodatabaseExportService"/> as a real integration test against the
/// actual GDAL native binding (no mocking GDAL) — same "write, then read back through the real
/// format reader" convention as <c>ShapefileExportServiceTests</c>, but reading back with OGR
/// instead of <c>ShapefileDataReader</c> since the output is a FileGDB, not a Shapefile.
/// </summary>
public sealed class FileGeodatabaseExportServiceTests
{
    // Same ESRI-dialect WKT SirgasUtmReprojector.SirgasUtmWkt(23) builds — a literal here keeps
    // this test independent of that private method.
    private const string SirgasUtmZone23SWkt =
        "PROJCS[\"SIRGAS_2000_UTM_Zone_23S\",GEOGCS[\"GCS_SIRGAS_2000\",DATUM[\"D_SIRGAS_2000\"," +
        "SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]," +
        "PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",10000000.0]," +
        "PARAMETER[\"Central_Meridian\",-45.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";
    private const int SirgasUtmZone23SSrid = 31983;

    private static GeoFeature PointFeature(string name, double x, double y, Dictionary<string, string>? attrs = null) => new()
    {
        Geometry = new GeoPoint(x, y, SirgasUtmZone23SSrid),
        Properties = new GeoFeatureProperties { Name = name, CustomAttributes = attrs ?? [] },
    };

    private static GeoFeature LineFeature(string name) => new()
    {
        Geometry = new GeoLineString([(500_000, 7_800_000), (500_100, 7_800_100)], SirgasUtmZone23SSrid),
        Properties = new GeoFeatureProperties { Name = name },
    };

    private static GeoFeature PolygonFeature(string name) => new()
    {
        Geometry = new GeoPolygon(
            [(500_000, 7_800_000), (500_000, 7_800_100), (500_100, 7_800_100), (500_100, 7_800_000), (500_000, 7_800_000)],
            SirgasUtmZone23SSrid),
        Properties = new GeoFeatureProperties { Name = name },
    };

    /// <summary>Unzips <paramref name="zip"/> into a fresh temp dir and returns the .gdb path inside it — caller must delete the parent dir.</summary>
    private static string UnzipToGdb(byte[] zip)
    {
        var dir = Path.Combine(Path.GetTempPath(), "fgdb_export_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ZipFile.ExtractToDirectory(new MemoryStream(zip), dir);
        return Path.Combine(dir, "export.gdb");
    }

    private static List<string> ReadLayerNames(string gdbPath)
    {
        GdalBase.ConfigureAll();
        using var dataSource = Ogr.Open(gdbPath, 0);
        var names = new List<string>();
        for (var i = 0; i < dataSource.GetLayerCount(); i++)
            names.Add(dataSource.GetLayerByIndex(i).GetName());
        return names;
    }

    [Fact]
    public void ExportToZip_AllLayersEmpty_Throws()
    {
        var sut = new FileGeodatabaseExportService();
        var layers = new Dictionary<string, IReadOnlyList<GeoFeature>>(StringComparer.Ordinal)
        {
            ["PONNOT"] = [],
        };

        var act = () => sut.ExportToZip(layers, SirgasUtmZone23SWkt, SirgasUtmZone23SSrid);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ExportToZip_NullLayeredFeatures_ThrowsArgumentNullException()
    {
        var sut = new FileGeodatabaseExportService();

        var act = () => sut.ExportToZip(null!, SirgasUtmZone23SWkt, SirgasUtmZone23SSrid);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExportToZip_NullOrWhitespaceTargetCrsWkt_ThrowsArgumentException(string? wkt)
    {
        var sut = new FileGeodatabaseExportService();
        var layers = new Dictionary<string, IReadOnlyList<GeoFeature>>(StringComparer.Ordinal)
        {
            ["PONNOT"] = [PointFeature("A", 500_000, 7_800_000)],
        };

        var act = () => sut.ExportToZip(layers, wkt!, SirgasUtmZone23SSrid);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ExportToZip_UnsupportedGeometryTypeInFirstFeature_ThrowsInvalidOperationException()
    {
        var sut = new FileGeodatabaseExportService();
        var layers = new Dictionary<string, IReadOnlyList<GeoFeature>>(StringComparer.Ordinal)
        {
            ["UC"] = [PolygonFeature("unsupported")],
        };

        var act = () => sut.ExportToZip(layers, SirgasUtmZone23SWkt, SirgasUtmZone23SSrid);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ExportToZip_SinglePointLayer_ProducesAValidZipWithARealFileGdb()
    {
        var sut = new FileGeodatabaseExportService();
        var layers = new Dictionary<string, IReadOnlyList<GeoFeature>>(StringComparer.Ordinal)
        {
            ["PONNOT"] = [PointFeature("Pole 1", 500_000, 7_800_000), PointFeature("Pole 2", 500_100, 7_800_100)],
        };

        var zip = sut.ExportToZip(layers, SirgasUtmZone23SWkt, SirgasUtmZone23SSrid);

        zip.Should().NotBeEmpty();
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        archive.Entries.Should().Contain(e => e.FullName.StartsWith("export.gdb/", StringComparison.Ordinal));
        archive.Entries.Should().Contain(e => e.Name.EndsWith(".gdbtable", StringComparison.Ordinal));
    }

    [Fact]
    public void ExportToZip_PointLayer_RoundTripsGeometryAndAttributesThroughOgr()
    {
        var sut = new FileGeodatabaseExportService();
        var layers = new Dictionary<string, IReadOnlyList<GeoFeature>>(StringComparer.Ordinal)
        {
            ["PONNOT"] = [PointFeature("Pole 1", 500_000, 7_800_000, new Dictionary<string, string>(StringComparer.Ordinal) { ["COD_ID"] = "P1" })],
        };

        var zip = sut.ExportToZip(layers, SirgasUtmZone23SWkt, SirgasUtmZone23SSrid);
        var gdbPath = UnzipToGdb(zip);
        var parentDir = Path.GetDirectoryName(gdbPath)!;
        try
        {
            GdalBase.ConfigureAll();
            using var dataSource = Ogr.Open(gdbPath, 0);
            using var layer = dataSource.GetLayerByName("PONNOT");
            layer.GetFeatureCount(1).Should().Be(1);

            using var ogrFeature = layer.GetNextFeature();
            ogrFeature.GetFieldAsString("NAME").Should().Be("Pole 1");
            ogrFeature.GetFieldAsString("COD_ID").Should().Be("P1");

            using var geometry = ogrFeature.GetGeometryRef();
            geometry.GetGeometryType().Should().Be(wkbGeometryType.wkbPoint);
            geometry.GetX(0).Should().BeApproximately(500_000, 0.01);
            geometry.GetY(0).Should().BeApproximately(7_800_000, 0.01);
        }
        finally { Directory.Delete(parentDir, recursive: true); }
    }

    [Fact]
    public void ExportToZip_LineStringLayer_RoundTripsWithoutThrowing()
    {
        var sut = new FileGeodatabaseExportService();
        var layers = new Dictionary<string, IReadOnlyList<GeoFeature>>(StringComparer.Ordinal)
        {
            ["SEGCON"] = [LineFeature("Segment 1")],
        };

        var act = () => sut.ExportToZip(layers, SirgasUtmZone23SWkt, SirgasUtmZone23SSrid);

        act.Should().NotThrow();
    }

    [Fact]
    public void ExportToZip_MultipleLayers_AllAppearInTheSameGdb()
    {
        var sut = new FileGeodatabaseExportService();
        var layers = new Dictionary<string, IReadOnlyList<GeoFeature>>(StringComparer.Ordinal)
        {
            ["PONNOT"] = [PointFeature("Pole 1", 500_000, 7_800_000)],
            ["SEGCON"] = [LineFeature("Segment 1")],
        };

        var zip = sut.ExportToZip(layers, SirgasUtmZone23SWkt, SirgasUtmZone23SSrid);
        var gdbPath = UnzipToGdb(zip);
        var parentDir = Path.GetDirectoryName(gdbPath)!;
        try
        {
            ReadLayerNames(gdbPath).Should().BeEquivalentTo(["PONNOT", "SEGCON"]);
        }
        finally { Directory.Delete(parentDir, recursive: true); }
    }

    [Fact]
    public void ExportToZip_FeaturesWithDifferentAttributeKeys_EveryFieldIsCreated()
    {
        var sut = new FileGeodatabaseExportService();
        var layers = new Dictionary<string, IReadOnlyList<GeoFeature>>(StringComparer.Ordinal)
        {
            ["PONNOT"] =
            [
                PointFeature("A", 500_000, 7_800_000, new Dictionary<string, string>(StringComparer.Ordinal) { ["foo"] = "1" }),
                PointFeature("B", 500_100, 7_800_100, new Dictionary<string, string>(StringComparer.Ordinal) { ["bar"] = "2" }),
            ],
        };

        var zip = sut.ExportToZip(layers, SirgasUtmZone23SWkt, SirgasUtmZone23SSrid);
        var gdbPath = UnzipToGdb(zip);
        var parentDir = Path.GetDirectoryName(gdbPath)!;
        try
        {
            GdalBase.ConfigureAll();
            using var dataSource = Ogr.Open(gdbPath, 0);
            using var layer = dataSource.GetLayerByName("PONNOT");
            using var layerDefn = layer.GetLayerDefn();
            var fieldNames = Enumerable.Range(0, layerDefn.GetFieldCount())
                .Select(i => layerDefn.GetFieldDefn(i).GetName())
                .ToList();

            fieldNames.Should().Contain(["NAME", "FOO", "BAR"]);
        }
        finally { Directory.Delete(parentDir, recursive: true); }
    }

    [Theory]
    [InlineData(new[] { "short" }, new[] { "SHORT" })]
    [InlineData(new[] { "name" }, new[] { "NAME1" })] // collides case-insensitively with the reserved NAME field
    [InlineData(new[] { "1leading_digit" }, new[] { "F_1LEADING_DIGIT" })]
    public void BuildFieldNameMap_SanitizesFieldNames(string[] keys, string[] expectedValues)
    {
        var map = FileGeodatabaseExportService.BuildFieldNameMap(keys);

        map.Values.Should().BeEquivalentTo(expectedValues);
    }

    [Fact]
    public void BuildFieldNameMap_LongKey_IsTruncatedToSixtyFourChars()
    {
        var longKey = new string('A', 100);

        var map = FileGeodatabaseExportService.BuildFieldNameMap([longKey]);

        map[longKey].Length.Should().Be(64);
    }

    [Fact]
    public void BuildFieldNameMap_KeysThatCollideAfterSanitization_GetDistinctSuffixes()
    {
        // "!" is stripped by sanitization, so both keys sanitize to the same "FOOBAR" base.
        var map = FileGeodatabaseExportService.BuildFieldNameMap(["foo!bar", "foobar"]);

        map.Values.Should().OnlyHaveUniqueItems();
        map["foo!bar"].Should().Be("FOOBAR");
        map["foobar"].Should().Be("FOOBAR1");
    }

    [Fact]
    public void BuildFieldNameMap_EmptyAfterSanitization_FallsBackToFIELD()
    {
        var map = FileGeodatabaseExportService.BuildFieldNameMap(["!!!"]);

        map["!!!"].Should().Be("FIELD");
    }

    [Fact]
    public void BuildFieldNameMap_NullKeys_ThrowsArgumentNullException()
    {
        var act = () => FileGeodatabaseExportService.BuildFieldNameMap(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
