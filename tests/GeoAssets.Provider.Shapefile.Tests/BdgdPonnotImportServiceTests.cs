using System.IO.Compression;
using FluentAssertions;
using GeoAssets.Core.Bdgd;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace GeoAssets.Provider.Shapefile.Tests;

/// <summary>
/// Covers <see cref="BdgdPonnotImportService"/> (XD01-257) end-to-end through real Shapefile ZIP
/// archives — the same <see cref="ShapefileDataWriter"/>/<c>.prj</c>-sidecar fixture technique
/// <see cref="ShapefileParserProjectionTests"/> already uses — rather than mocking
/// <see cref="ShapefileParser"/>, since the whole point is proving the archive → geo_entity path
/// works, including the SIRGAS 2000 UTM → WGS84 reprojection the AC calls out by name.
/// </summary>
public class BdgdPonnotImportServiceTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    private sealed record PonnotRecord(double X, double Y, string CodId, string TipEst, string AltTot, string CapTrc);

    /// <summary>Builds a BDGD-shaped Shapefile ZIP (COD_ID/TIP_EST/ALT_TOT/CAP_TRC columns), optionally with a <c>.prj</c>.</summary>
    private static async Task<byte[]> BuildPonnotArchiveAsync(IReadOnlyList<PonnotRecord> records, string? prjWkt)
    {
        var features = records.Select(r =>
        {
            var table = new AttributesTable(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["COD_ID"] = r.CodId,
                ["TIP_EST"] = r.TipEst,
                ["ALT_TOT"] = r.AltTot,
                ["CAP_TRC"] = r.CapTrc,
            });
            return (IFeature)new Feature(Factory.CreatePoint(new Coordinate(r.X, r.Y)), table);
        }).ToList();

        var tempDir = Path.Combine(Path.GetTempPath(), "ponnot_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var basePath = Path.Combine(tempDir, "ponnot");
            var header = new DbaseFileHeader();
            header.AddColumn("COD_ID", 'C', 50, 0);
            header.AddColumn("TIP_EST", 'C', 50, 0);
            header.AddColumn("ALT_TOT", 'C', 20, 0);
            header.AddColumn("CAP_TRC", 'C', 20, 0);
            header.NumRecords = features.Count;

            var writer = new ShapefileDataWriter(basePath, Factory) { Header = header };
            writer.Write(features);

            if (prjWkt is not null)
                await File.WriteAllTextAsync(basePath + ".prj", prjWkt);

            var zipPath = Path.Combine(tempDir, "archive.zip");
            await using (var zip = await ZipFile.OpenAsync(zipPath, ZipArchiveMode.Create))
                foreach (var f in Directory.GetFiles(tempDir, "ponnot.*"))
                    await zip.CreateEntryFromFileAsync(f, Path.GetFileName(f));

            return await File.ReadAllBytesAsync(zipPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>A genuinely empty <see cref="IAssetProvider"/> to import into — built via the public
    /// <see cref="ShapefileProviderPlugin"/> entry point (the store itself is internal) from a
    /// throwaway single-feature archive, then immediately emptied.</summary>
    private static async Task<IAssetProvider> EmptyProviderAsync()
    {
        var seed = await BuildPonnotArchiveAsync(
            [new PonnotRecord(0, 0, "SEED", "x", "1", "1")], prjWkt: null);

        var config = new ProviderConfig();
        config.Set("name", "target");
        config.Set("archive_content", Convert.ToBase64String(seed));

        var provider = await new ShapefileProviderPlugin().CreateAsync(config, null!);
        foreach (var f in provider.GetAll().ToList())
            provider.Delete(f.Id);

        return provider;
    }

    [Fact]
    public async Task ImportAsync_WellFormedArchive_InsertsPonnotFeaturesWithMappedAttributes()
    {
        var archive = await BuildPonnotArchiveAsync(
            [new PonnotRecord(-46.633, -23.55, "PN0001", "Concreto DT", "11", "600")], prjWkt: null);
        var target = await EmptyProviderAsync();

        var result = await new BdgdPonnotImportService().ImportAsync(archive, target);

        result.Inserted.Should().Be(1);
        result.Updated.Should().Be(0);
        result.Skipped.Should().Be(0);

        var pole = target.GetByAssetType(BdgdAssetTypeIds.Ponnot.ToString()).Single();
        pole.Properties.CustomAttributes["COD_ID"].Should().Be("PN0001");
        pole.Properties.CustomAttributes["TIP_EST"].Should().Be("Concreto DT");
        pole.Properties.CustomAttributes["ALT_TOT"].Should().Be("11");
        pole.Properties.CustomAttributes["CAP_TRC"].Should().Be("600");
    }

    [Fact]
    public async Task ImportAsync_ReRunOnSameCodId_UpsertsRatherThanDuplicating()
    {
        var target = await EmptyProviderAsync();
        var sut = new BdgdPonnotImportService();

        var firstArchive = await BuildPonnotArchiveAsync(
            [new PonnotRecord(-46.633, -23.55, "PN0001", "Concreto DT", "11", "600")], prjWkt: null);
        var firstResult = await sut.ImportAsync(firstArchive, target);

        var secondArchive = await BuildPonnotArchiveAsync(
            [new PonnotRecord(-46.633, -23.55, "PN0001", "Concreto DT", "12", "650")], prjWkt: null);
        var secondResult = await sut.ImportAsync(secondArchive, target);

        firstResult.Inserted.Should().Be(1);
        secondResult.Inserted.Should().Be(0);
        secondResult.Updated.Should().Be(1);

        var poles = target.GetByAssetType(BdgdAssetTypeIds.Ponnot.ToString());
        poles.Should().HaveCount(1);
        poles[0].Properties.CustomAttributes["ALT_TOT"].Should().Be("12");
        poles[0].Properties.CustomAttributes["CAP_TRC"].Should().Be("650");
    }

    [Fact]
    public async Task ImportAsync_RecordMissingRequiredField_SkipsItAndReportsWarningWithoutFailingOthers()
    {
        var archive = await BuildPonnotArchiveAsync(
        [
            new PonnotRecord(-46.633, -23.55, "PN0001", "Concreto DT", "11", "600"),
            new PonnotRecord(-46.7, -23.6, "PN0002", "", "9", "300"),
        ], prjWkt: null);
        var target = await EmptyProviderAsync();

        var result = await new BdgdPonnotImportService().ImportAsync(archive, target);

        result.Inserted.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.Warnings.Should().ContainSingle(w => w.Contains("TIP_EST"));
    }

    [Fact]
    public async Task ImportAsync_SirgasUtmSourceCrs_RoundTripsCoordinatesWithinSubMeterTolerance()
    {
        // A known WGS84 point, projected to its own SIRGAS 2000 UTM zone via the exact
        // reprojector XD01-225 built for the write-side (export) direction — used here purely to
        // derive a realistic UTM easting/northing/zone to write as the shapefile's raw coordinate,
        // completely independent of ShapefileParser's own WKT-based .prj transform under test.
        var originalWgs84 = new GeoPoint(-43.2, -22.9); // Rio de Janeiro area
        var (easting, northing, _) = SirgasUtmReprojector.ToSirgasUtm(originalWgs84);
        var zone = SirgasUtmZoneResolver.ZoneFor(originalWgs84.Longitude);
        var prjWkt = SirgasUtmReprojector.WktFor(zone);

        var archive = await BuildPonnotArchiveAsync(
            [new PonnotRecord(easting, northing, "PN0001", "Concreto DT", "11", "600")], prjWkt);
        var target = await EmptyProviderAsync();

        await new BdgdPonnotImportService().ImportAsync(archive, target);

        var imported = target.GetByAssetType(BdgdAssetTypeIds.Ponnot.ToString()).Single();
        var importedPoint = (GeoPoint)imported.Geometry!;

        // Sub-meter tolerance in degrees: ~1e-5 deg ≈ 1.1m at the equator, tighter at this latitude.
        importedPoint.Longitude.Should().BeApproximately(originalWgs84.Longitude, 1e-5);
        importedPoint.Latitude.Should().BeApproximately(originalWgs84.Latitude, 1e-5);
    }
}
