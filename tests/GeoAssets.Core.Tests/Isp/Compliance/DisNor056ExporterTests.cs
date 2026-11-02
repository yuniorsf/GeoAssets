using System.Text;
using FluentAssertions;
using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.Compliance;

/// <summary>
/// Covers <see cref="DisNor056Exporter"/>'s PONNOT/COD_ID selection, SIRGAS 2000 UTM
/// reprojection hand-off to <see cref="IShapefileExportService"/>, and the DXF/Shapefile pair it
/// produces. The exact UTM transform math is already covered by
/// <c>SirgasUtmReprojectorTests</c> — this only checks the reprojected values landed in a
/// plausible UTM magnitude range and differ from the raw WGS84 input.
/// </summary>
public class DisNor056ExporterTests
{
    private const string CodIdKey = "COD_ID";

    private sealed class FakeShapefileExportService : IShapefileExportService
    {
        public IReadOnlyList<GeoFeature>? CapturedFeatures { get; private set; }
        public string? CapturedTargetCrsWkt { get; private set; }
        public int? CapturedTargetSrid { get; private set; }
        public byte[] ReturnValue { get; set; } = [1, 2, 3];

        public byte[] ExportToZip(IReadOnlyList<GeoFeature> features, string? targetCrsWkt = null, int? targetSrid = null)
        {
            CapturedFeatures = features;
            CapturedTargetCrsWkt = targetCrsWkt;
            CapturedTargetSrid = targetSrid;
            return ReturnValue;
        }
    }

    private static GeoFeature PonnotPole(string codId, double lon = -46.633, double lat = -23.55) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Geometry = new GeoPoint(lon, lat),
        Properties = new GeoFeatureProperties
        {
            AssetTypeId = BdgdAssetTypeIds.Ponnot.ToString(),
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { [CodIdKey] = codId },
        },
    };

    private static GeoFeature NonPonnotPole(string codId = "OTHER") => new()
    {
        Id = Guid.NewGuid().ToString(),
        Geometry = new GeoPoint(-46.633, -23.55),
        Properties = new GeoFeatureProperties
        {
            AssetTypeId = Guid.NewGuid().ToString(),
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { [CodIdKey] = codId },
        },
    };

    private static GeoFeature PonnotPoleWithoutCodId() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Geometry = new GeoPoint(-46.633, -23.55),
        Properties = new GeoFeatureProperties
        {
            AssetTypeId = BdgdAssetTypeIds.Ponnot.ToString(),
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal),
        },
    };

    private static GeoFeature PonnotPoleWithNonPointGeometry() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Geometry = new GeoLineString([(0, 0), (1, 1)]),
        Properties = new GeoFeatureProperties
        {
            AssetTypeId = BdgdAssetTypeIds.Ponnot.ToString(),
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { [CodIdKey] = "LINE-1" },
        },
    };

    [Fact]
    public void Export_WellFormedPonnotPolesWithCodId_ProducesExactlyTwoNamedFiles()
    {
        var sut = new DisNor056Exporter(new FakeShapefileExportService());
        var poles = new List<GeoFeature> { PonnotPole("POLE-1"), PonnotPole("POLE-2") };

        var result = sut.Export(poles);

        result.Files.Should().HaveCount(2);
        result.Files.Select(f => f.FileName).Should().BeEquivalentTo(
            ["dis-nor-056-poles.dxf", "dis-nor-056-poles-shapefile.zip"]);
    }

    [Fact]
    public void Export_ShapefileEntry_IsExactlyWhatTheShapefileServiceReturned()
    {
        var fake = new FakeShapefileExportService { ReturnValue = [9, 9, 9] };
        var sut = new DisNor056Exporter(fake);

        var result = sut.Export([PonnotPole("POLE-1")]);

        result.Files.Single(f => string.Equals(f.FileName, "dis-nor-056-poles-shapefile.zip", StringComparison.Ordinal)).Content.Should().Equal(9, 9, 9);
    }

    [Fact]
    public void Export_ExcludesNonPonnotFeatures()
    {
        var fake = new FakeShapefileExportService();
        var sut = new DisNor056Exporter(fake);

        sut.Export([PonnotPole("POLE-1"), NonPonnotPole("POLE-2")]);

        fake.CapturedFeatures.Should().HaveCount(1);
        fake.CapturedFeatures![0].Properties.CustomAttributes[CodIdKey].Should().Be("POLE-1");
    }

    [Fact]
    public void Export_ExcludesPonnotFeaturesMissingCodId()
    {
        var fake = new FakeShapefileExportService();
        var sut = new DisNor056Exporter(fake);

        sut.Export([PonnotPole("POLE-1"), PonnotPoleWithoutCodId()]);

        fake.CapturedFeatures.Should().HaveCount(1);
        fake.CapturedFeatures![0].Properties.CustomAttributes[CodIdKey].Should().Be("POLE-1");
    }

    [Fact]
    public void Export_ExcludesPonnotFeaturesWithNonPointGeometry()
    {
        var fake = new FakeShapefileExportService();
        var sut = new DisNor056Exporter(fake);

        sut.Export([PonnotPole("POLE-1"), PonnotPoleWithNonPointGeometry()]);

        fake.CapturedFeatures.Should().HaveCount(1);
        fake.CapturedFeatures![0].Properties.CustomAttributes[CodIdKey].Should().Be("POLE-1");
    }

    [Fact]
    public void Export_NoQualifyingFeatures_ThrowsInvalidOperationException()
    {
        var sut = new DisNor056Exporter(new FakeShapefileExportService());

        var act = () => sut.Export([NonPonnotPole(), PonnotPoleWithoutCodId()]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Export_EmptyFeatureList_ThrowsInvalidOperationException()
    {
        var sut = new DisNor056Exporter(new FakeShapefileExportService());

        var act = () => sut.Export([]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Export_NullFeatureList_ThrowsArgumentNullException()
    {
        var sut = new DisNor056Exporter(new FakeShapefileExportService());

        var act = () => sut.Export(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Export_PassesUtmReprojectedCoordinatesToShapefileService()
    {
        var fake = new FakeShapefileExportService();
        var sut = new DisNor056Exporter(fake);
        var pole = PonnotPole("POLE-1", lon: -46.633, lat: -23.55);

        sut.Export([pole]);

        fake.CapturedFeatures.Should().HaveCount(1);
        var reprojected = (GeoPoint)fake.CapturedFeatures![0].Geometry!;

        // Easting/northing, not longitude/latitude anymore — a gross reprojection bug would
        // leave these looking like raw WGS84 degrees instead.
        reprojected.Longitude.Should().NotBe(pole.Geometry is GeoPoint original ? original.Longitude : 0);
        reprojected.Longitude.Should().BeInRange(100_000, 900_000);
        reprojected.Latitude.Should().BeInRange(6_000_000, 10_000_000);

        fake.CapturedTargetCrsWkt.Should().NotBeNullOrWhiteSpace();
        fake.CapturedTargetCrsWkt.Should().Contain("SIRGAS_2000");
        fake.CapturedTargetSrid.Should().Be(31983); // São Paulo falls in SIRGAS 2000 / UTM zone 23S
    }

    [Fact]
    public void Export_ReprojectedFeature_KeepsTheOriginalPolesPropertiesInstance()
    {
        var fake = new FakeShapefileExportService();
        var sut = new DisNor056Exporter(fake);
        var pole = PonnotPole("POLE-1");

        sut.Export([pole]);

        fake.CapturedFeatures![0].Properties.Should().BeSameAs(pole.Properties);
    }

    [Fact]
    public void Export_DxfFile_ContainsEachPolesCodIdLabel()
    {
        var sut = new DisNor056Exporter(new FakeShapefileExportService());

        var result = sut.Export([PonnotPole("POLE-LABEL-42"), PonnotPole("POLE-LABEL-99")]);

        var dxfText = Encoding.ASCII.GetString(result.Files.Single(f => string.Equals(f.FileName, "dis-nor-056-poles.dxf", StringComparison.Ordinal)).Content);
        dxfText.Should().Contain("POLE-LABEL-42");
        dxfText.Should().Contain("POLE-LABEL-99");
    }
}
