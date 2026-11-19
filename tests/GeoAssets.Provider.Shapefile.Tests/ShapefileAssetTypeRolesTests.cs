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
/// The asset types a shapefile store exposes carry no <see cref="AssetType.Roles"/> (XD01-367):
/// roles are authored on a server, never derived from a shapefile.
/// </summary>
public class ShapefileAssetTypeRolesTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    private static async Task<IAssetProvider> LoadProviderAsync()
    {
        var attributes = new AttributesTable(new Dictionary<string, object>(StringComparer.Ordinal) { ["NAME"] = "A" });
        var features = new List<IFeature> { new Feature(Factory.CreatePoint(new Coordinate(1, 1)), attributes) };

        var tempDir = Path.Combine(Path.GetTempPath(), "shp_roles_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var header = new DbaseFileHeader();
            header.AddColumn("NAME", 'C', 50, 0);
            header.NumRecords = features.Count;
            new ShapefileDataWriter(Path.Combine(tempDir, "fixture"), Factory) { Header = header }.Write(features);

            var zipPath = Path.Combine(tempDir, "archive.zip");
            await using (var zip = await ZipFile.OpenAsync(zipPath, ZipArchiveMode.Create))
                foreach (var f in Directory.GetFiles(tempDir, "fixture.*"))
                    await zip.CreateEntryFromFileAsync(f, Path.GetFileName(f));

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
    public async Task GetAssetTypes_OnAShapefileStore_ExposesNoRoles()
    {
        var provider = await LoadProviderAsync();

        provider.GetAssetTypes().Should().NotBeEmpty();
        provider.GetAssetTypes().Should().OnlyContain(t => t.Roles.Count == 0);
    }

    [Fact]
    public async Task GetAssetTypesWithRole_OnAShapefileStore_IsAlwaysEmpty()
    {
        var provider = await LoadProviderAsync();

        provider.GetAssetTypesWithRole("occupiable-support").Should().BeEmpty();
    }
}
