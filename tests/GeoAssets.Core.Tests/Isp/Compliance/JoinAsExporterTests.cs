using System.Globalization;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.Compliance;

/// <summary>
/// Covers <see cref="JoinAsExporter"/>'s PONNOT/COD_ID selection (independently implemented from
/// <see cref="DisNor056Exporter"/> — AC3) and its CSV/XML manifest content, including the fixed
/// <see cref="PoleSafetyDistances"/> constants every record must carry.
/// </summary>
public class JoinAsExporterTests
{
    private const string CodIdKey = "COD_ID";

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

    private static string Csv(JoinAsExporter sut, IReadOnlyList<GeoFeature> poles) =>
        Encoding.UTF8.GetString(sut.Export(poles).Files.Single(f => string.Equals(f.FileName, "join-as-manifest.csv", StringComparison.Ordinal)).Content);

    private static string Xml(JoinAsExporter sut, IReadOnlyList<GeoFeature> poles) =>
        Encoding.UTF8.GetString(sut.Export(poles).Files.Single(f => string.Equals(f.FileName, "join-as-manifest.xml", StringComparison.Ordinal)).Content);

    [Fact]
    public void Export_WellFormedPonnotPolesWithCodId_ProducesExactlyTwoNamedFiles()
    {
        var sut = new JoinAsExporter();
        var poles = new List<GeoFeature> { PonnotPole("POLE-1"), PonnotPole("POLE-2") };

        var result = sut.Export(poles);

        result.Files.Should().HaveCount(2);
        result.Files.Select(f => f.FileName).Should().BeEquivalentTo(
            ["join-as-manifest.csv", "join-as-manifest.xml"]);
    }

    [Fact]
    public void Export_Csv_HasExpectedHeaderAndOneRowPerPole()
    {
        var sut = new JoinAsExporter();
        var pole1 = PonnotPole("POLE-1", lon: -46.633, lat: -23.55);
        var pole2 = PonnotPole("POLE-2", lon: -43.2, lat: -22.9);

        var csv = Csv(sut, [pole1, pole2]);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("CodId,Longitude,Latitude,SecondarySafetyDistanceMeters,MediumVoltageSafetyDistanceMeters");
        lines.Should().HaveCount(3); // header + 2 rows
        lines[1].Should().Be(string.Create(CultureInfo.InvariantCulture,
            $"POLE-1,{-46.633:F6},{-23.55:F6},{PoleSafetyDistances.SecondaryMeters:F2},{PoleSafetyDistances.MediumVoltageMeters:F2}"));
        lines[2].Should().Be(string.Create(CultureInfo.InvariantCulture,
            $"POLE-2,{-43.2:F6},{-22.9:F6},{PoleSafetyDistances.SecondaryMeters:F2},{PoleSafetyDistances.MediumVoltageMeters:F2}"));
    }

    [Fact]
    public void Export_Csv_SafetyDistanceConstants_AreCorrectOnEveryRecord()
    {
        var sut = new JoinAsExporter();
        var poles = new List<GeoFeature> { PonnotPole("POLE-1"), PonnotPole("POLE-2"), PonnotPole("POLE-3") };

        var lines = Csv(sut, poles).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1);

        lines.Should().OnlyContain(line => line.EndsWith("0.60,1.50", StringComparison.Ordinal));
    }

    [Fact]
    public void Export_Csv_CodIdContainingComma_IsQuoted()
    {
        var sut = new JoinAsExporter();

        var csv = Csv(sut, [PonnotPole("POLE,1")]);
        var row = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1];

        row.Should().StartWith("\"POLE,1\",");
    }

    [Fact]
    public void Export_Csv_CodIdWithoutComma_IsNotQuoted()
    {
        var sut = new JoinAsExporter();

        var csv = Csv(sut, [PonnotPole("POLE-1")]);
        var row = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1];

        row.Should().StartWith("POLE-1,");
    }

    [Fact]
    public void Export_Xml_HasExpectedRootAndOnePoleElementPerPoleInFieldOrder()
    {
        var sut = new JoinAsExporter();
        var pole = PonnotPole("POLE-1", lon: -46.633, lat: -23.55);

        var document = XDocument.Parse(Xml(sut, [pole]));

        document.Root!.Name.LocalName.Should().Be("JoinAsManifest");
        var poleElement = document.Root.Elements("Pole").Should().ContainSingle().Subject;
        poleElement.Elements().Select(e => e.Name.LocalName).Should().Equal(
            "CodId", "Longitude", "Latitude", "SecondarySafetyDistanceMeters", "MediumVoltageSafetyDistanceMeters");

        poleElement.Element("CodId")!.Value.Should().Be("POLE-1");
        poleElement.Element("Longitude")!.Value.Should().Be((-46.633).ToString("F6", CultureInfo.InvariantCulture));
        poleElement.Element("Latitude")!.Value.Should().Be((-23.55).ToString("F6", CultureInfo.InvariantCulture));
        poleElement.Element("SecondarySafetyDistanceMeters")!.Value.Should().Be(
            PoleSafetyDistances.SecondaryMeters.ToString("F2", CultureInfo.InvariantCulture));
        poleElement.Element("MediumVoltageSafetyDistanceMeters")!.Value.Should().Be(
            PoleSafetyDistances.MediumVoltageMeters.ToString("F2", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Export_Xml_MultiplePoles_ProducesOnePoleElementEach()
    {
        var sut = new JoinAsExporter();
        var poles = new List<GeoFeature> { PonnotPole("POLE-1"), PonnotPole("POLE-2") };

        var document = XDocument.Parse(Xml(sut, poles));

        document.Root!.Elements("Pole").Select(p => p.Element("CodId")!.Value).Should().Equal("POLE-1", "POLE-2");
    }

    [Fact]
    public void Export_ExcludesNonPonnotFeatures()
    {
        var sut = new JoinAsExporter();

        var document = XDocument.Parse(Xml(sut, [PonnotPole("POLE-1"), NonPonnotPole("POLE-2")]));

        document.Root!.Elements("Pole").Should().ContainSingle(p => p.Element("CodId")!.Value == "POLE-1");
    }

    [Fact]
    public void Export_ExcludesPonnotFeaturesMissingCodId()
    {
        var sut = new JoinAsExporter();

        var document = XDocument.Parse(Xml(sut, [PonnotPole("POLE-1"), PonnotPoleWithoutCodId()]));

        document.Root!.Elements("Pole").Should().ContainSingle(p => p.Element("CodId")!.Value == "POLE-1");
    }

    [Fact]
    public void Export_ExcludesPonnotFeaturesWithNonPointGeometry()
    {
        var sut = new JoinAsExporter();

        var document = XDocument.Parse(Xml(sut, [PonnotPole("POLE-1"), PonnotPoleWithNonPointGeometry()]));

        document.Root!.Elements("Pole").Should().ContainSingle(p => p.Element("CodId")!.Value == "POLE-1");
    }

    [Fact]
    public void Export_NoQualifyingFeatures_ThrowsInvalidOperationException()
    {
        var sut = new JoinAsExporter();

        var act = () => sut.Export([NonPonnotPole(), PonnotPoleWithoutCodId()]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Export_EmptyFeatureList_ThrowsInvalidOperationException()
    {
        var sut = new JoinAsExporter();

        var act = () => sut.Export([]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Export_NullFeatureList_ThrowsArgumentNullException()
    {
        var sut = new JoinAsExporter();

        var act = () => sut.Export(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
