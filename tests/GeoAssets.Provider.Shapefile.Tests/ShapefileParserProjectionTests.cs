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
/// Regression coverage for reprojecting a shapefile's coordinates to WGS84 via its <c>.prj</c>
/// sidecar before handing them to <see cref="GeoAssets.Core.Models.Geometry.GeoGeometry.FromNts"/>,
/// which always treats coordinates as WGS84 lon/lat. Without this, a shapefile exported in a
/// projected CRS (e.g. UTM, in meters) imports its attributes correctly but its geometry lands
/// far outside any sane map view, so nothing renders. The WKT and raw coordinate below are taken
/// from the real-world fixture (SAD69 UTM Zone 24S) this bug was diagnosed against.
/// </summary>
public class ShapefileParserProjectionTests
{
    private const string Sad69Utm24SWkt =
        "PROJCS[\"SAD_1969_UTM_Zone_24S\",GEOGCS[\"GCS_South_American_1969\",DATUM[\"D_South_American_1969\"," +
        "SPHEROID[\"GRS_1967_Truncated\",6378160.0,298.25]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]," +
        "PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",500000.0],PARAMETER[\"False_Northing\",10000000.0]," +
        "PARAMETER[\"Central_Meridian\",-42.0],PARAMETER[\"Scale_Factor\",0.9996],PARAMETER[\"Latitude_Of_Origin\",0.0],UNIT[\"Meter\",1.0]]";

    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    private static async Task<IAssetProvider> LoadFixtureProviderAsync(double x, double y, string? prjWkt)
    {
        var point = Factory.CreatePoint(new Coordinate(x, y));
        var features = new List<IFeature> { new Feature(point, new AttributesTable { { "NAME", "Pole" } }) };

        var tempDir = Path.Combine(Path.GetTempPath(), "shp_proj_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var basePath = Path.Combine(tempDir, "fixture");
            var header = new DbaseFileHeader();
            header.AddColumn("NAME", 'C', 50, 0);
            header.NumRecords = features.Count;

            var writer = new ShapefileDataWriter(basePath, Factory) { Header = header };
            writer.Write(features);

            if (prjWkt is not null)
                await File.WriteAllTextAsync(basePath + ".prj", prjWkt);

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
    public async Task CreateAsync_PrjInProjectedCrs_ReprojectsGeometryToWgs84()
    {
        var provider = await LoadFixtureProviderAsync(740141.92, 8784119.52, Sad69Utm24SWkt);

        var feature = provider.GetAll().Single();
        var coord = feature.Geometry!.NtsGeometry.Coordinate;

        coord.X.Should().BeApproximately(-39.80235761934942, 1e-6);
        coord.Y.Should().BeApproximately(-10.991121102173132, 1e-6);
    }

    [Fact]
    public async Task CreateAsync_NoPrjSidecar_UsesRawCoordinatesUnchanged()
    {
        var provider = await LoadFixtureProviderAsync(-39.8, -10.99, prjWkt: null);

        var feature = provider.GetAll().Single();
        var coord = feature.Geometry!.NtsGeometry.Coordinate;

        coord.X.Should().Be(-39.8);
        coord.Y.Should().Be(-10.99);
    }
}
