using FluentAssertions;
using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Bdgd;

/// <summary>
/// Covers <see cref="BdgdFileGeodatabaseExporter"/>'s layer grouping, SIRGAS 2000 UTM
/// reprojection hand-off to <see cref="IFileGeodatabaseExportService"/>, and its guard clauses.
/// The exact UTM transform math is already covered by <c>SirgasUtmReprojectorTests</c> — this
/// only checks the reprojected values landed in a plausible UTM magnitude range and differ from
/// the raw WGS84 input, same convention as <c>DisNor056ExporterTests</c>.
/// </summary>
public class BdgdFileGeodatabaseExporterTests
{
    private const string CodIdKey = "COD_ID";

    private sealed class FakeFileGeodatabaseExportService : IFileGeodatabaseExportService
    {
        public IReadOnlyDictionary<string, IReadOnlyList<GeoFeature>>? CapturedLayeredFeatures { get; private set; }
        public string? CapturedTargetCrsWkt { get; private set; }
        public int? CapturedTargetSrid { get; private set; }
        public byte[] ReturnValue { get; set; } = [1, 2, 3];

        public byte[] ExportToZip(IReadOnlyDictionary<string, IReadOnlyList<GeoFeature>> layeredFeatures, string targetCrsWkt, int targetSrid)
        {
            CapturedLayeredFeatures = layeredFeatures;
            CapturedTargetCrsWkt = targetCrsWkt;
            CapturedTargetSrid = targetSrid;
            return ReturnValue;
        }
    }

    private static GeoFeature BdgdPoint(Guid assetTypeId, string codId, double lon = -46.633, double lat = -23.55) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Geometry = new GeoPoint(lon, lat),
        Properties = new GeoFeatureProperties
        {
            AssetTypeId = assetTypeId.ToString(),
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { [CodIdKey] = codId },
        },
    };

    private static GeoFeature BdgdLine(Guid assetTypeId, string codId, double lon = -46.633, double lat = -23.55) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Geometry = new GeoLineString([(lon, lat), (lon + 0.01, lat + 0.01)]),
        Properties = new GeoFeatureProperties
        {
            AssetTypeId = assetTypeId.ToString(),
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { [CodIdKey] = codId },
        },
    };

    private static GeoFeature NonBdgdFeature() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Geometry = new GeoPoint(-46.633, -23.55),
        Properties = new GeoFeatureProperties { AssetTypeId = Guid.NewGuid().ToString() },
    };

    [Fact]
    public void Export_MixedBdgdTypes_GroupsEachIntoItsOwnNamedLayer()
    {
        var fake = new FakeFileGeodatabaseExportService();
        var sut = new BdgdFileGeodatabaseExporter(fake);

        sut.Export([
            BdgdPoint(BdgdAssetTypeIds.Ponnot, "P1"),
            BdgdPoint(BdgdAssetTypeIds.Untrd, "U1"),
            BdgdLine(BdgdAssetTypeIds.Segcon, "S1"),
        ]);

        fake.CapturedLayeredFeatures.Should().ContainKeys("PONNOT", "UNTRD", "SEGCON");
        fake.CapturedLayeredFeatures!["PONNOT"].Should().HaveCount(1);
        fake.CapturedLayeredFeatures["UNTRD"].Should().HaveCount(1);
        fake.CapturedLayeredFeatures["SEGCON"].Should().HaveCount(1);
    }

    [Fact]
    public void Export_ExcludesNonBdgdFeatures()
    {
        var fake = new FakeFileGeodatabaseExportService();
        var sut = new BdgdFileGeodatabaseExporter(fake);

        sut.Export([BdgdPoint(BdgdAssetTypeIds.Ponnot, "P1"), NonBdgdFeature()]);

        fake.CapturedLayeredFeatures!.Values.Sum(v => v.Count).Should().Be(1);
    }

    [Fact]
    public void Export_NoQualifyingFeatures_ThrowsInvalidOperationException()
    {
        var sut = new BdgdFileGeodatabaseExporter(new FakeFileGeodatabaseExportService());

        var act = () => sut.Export([NonBdgdFeature()]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Export_EmptyFeatureList_ThrowsInvalidOperationException()
    {
        var sut = new BdgdFileGeodatabaseExporter(new FakeFileGeodatabaseExportService());

        var act = () => sut.Export([]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Export_NullFeatureList_ThrowsArgumentNullException()
    {
        var sut = new BdgdFileGeodatabaseExporter(new FakeFileGeodatabaseExportService());

        var act = () => sut.Export(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Export_OnlyLineStringBdgdFeatures_ThrowsBecauseNoPointAnchorsTheUtmZone()
    {
        var sut = new BdgdFileGeodatabaseExporter(new FakeFileGeodatabaseExportService());

        var act = () => sut.Export([BdgdLine(BdgdAssetTypeIds.Segcon, "S1")]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Export_BdgdFeatureWithNullGeometry_ThrowsInvalidOperationException()
    {
        var sut = new BdgdFileGeodatabaseExporter(new FakeFileGeodatabaseExportService());
        var anchor = BdgdPoint(BdgdAssetTypeIds.Ponnot, "P1");
        var noGeometry = new GeoFeature
        {
            Id = Guid.NewGuid().ToString(),
            Geometry = null,
            Properties = new GeoFeatureProperties { AssetTypeId = BdgdAssetTypeIds.Untrd.ToString() },
        };

        var act = () => sut.Export([anchor, noGeometry]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Export_BdgdFeatureWithUnsupportedGeometryType_ThrowsInvalidOperationException()
    {
        var sut = new BdgdFileGeodatabaseExporter(new FakeFileGeodatabaseExportService());
        var anchor = BdgdPoint(BdgdAssetTypeIds.Ponnot, "P1");
        var polygonFeature = new GeoFeature
        {
            Id = Guid.NewGuid().ToString(),
            Geometry = new GeoPolygon([(-46.0, -23.0), (-46.0, -23.1), (-45.9, -23.1), (-46.0, -23.0)]),
            Properties = new GeoFeatureProperties { AssetTypeId = BdgdAssetTypeIds.Untrd.ToString() },
        };

        var act = () => sut.Export([anchor, polygonFeature]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Export_PassesUtmReprojectedCoordinatesForPointFeature()
    {
        var fake = new FakeFileGeodatabaseExportService();
        var sut = new BdgdFileGeodatabaseExporter(fake);
        var pole = BdgdPoint(BdgdAssetTypeIds.Ponnot, "P1", lon: -46.633, lat: -23.55);

        sut.Export([pole]);

        var reprojected = (GeoPoint)fake.CapturedLayeredFeatures!["PONNOT"][0].Geometry!;
        reprojected.Longitude.Should().NotBe(((GeoPoint)pole.Geometry!).Longitude);
        reprojected.Longitude.Should().BeInRange(100_000, 900_000);
        reprojected.Latitude.Should().BeInRange(6_000_000, 10_000_000);

        fake.CapturedTargetCrsWkt.Should().Contain("SIRGAS_2000");
        fake.CapturedTargetSrid.Should().Be(31983); // São Paulo falls in SIRGAS 2000 / UTM zone 23S
    }

    [Fact]
    public void Export_PassesUtmReprojectedCoordinatesForLineStringFeature()
    {
        var fake = new FakeFileGeodatabaseExportService();
        var sut = new BdgdFileGeodatabaseExporter(fake);
        var anchor = BdgdPoint(BdgdAssetTypeIds.Ponnot, "P1", lon: -46.633, lat: -23.55);
        var segment = BdgdLine(BdgdAssetTypeIds.Segcon, "S1", lon: -46.633, lat: -23.55);

        sut.Export([anchor, segment]);

        var reprojected = (GeoLineString)fake.CapturedLayeredFeatures!["SEGCON"][0].Geometry!;
        var original = (GeoLineString)segment.Geometry!;
        reprojected.Coordinates.Should().HaveSameCount(original.Coordinates);
        foreach (var vertex in reprojected.Coordinates)
        {
            vertex[0].Should().BeInRange(100_000, 900_000);
            vertex[1].Should().BeInRange(6_000_000, 10_000_000);
        }
    }

    [Fact]
    public void Export_ReprojectedFeature_KeepsTheOriginalPropertiesInstance()
    {
        var fake = new FakeFileGeodatabaseExportService();
        var sut = new BdgdFileGeodatabaseExporter(fake);
        var pole = BdgdPoint(BdgdAssetTypeIds.Ponnot, "P1");

        sut.Export([pole]);

        fake.CapturedLayeredFeatures!["PONNOT"][0].Properties.Should().BeSameAs(pole.Properties);
    }

    [Fact]
    public void Export_ReturnsExactlyWhatTheFileGeodatabaseServiceReturned()
    {
        var fake = new FakeFileGeodatabaseExportService { ReturnValue = [9, 9, 9] };
        var sut = new BdgdFileGeodatabaseExporter(fake);

        var result = sut.Export([BdgdPoint(BdgdAssetTypeIds.Ponnot, "P1")]);

        result.Should().Equal(9, 9, 9);
    }
}
