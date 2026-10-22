using System.IO.Compression;
using FluentAssertions;
using GeoAssets.Core.Models;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace GeoAssets.Provider.Shapefile.Tests;

/// <summary>
/// Covers <see cref="ShapefileProviderPlugin.CreateAsync"/>'s path-based reconnect: when a Project
/// is reopened, <c>archive_content</c> (deliberately never persisted — see
/// <see cref="ProviderConfig.ToReconnectValues"/>) is absent, so the plugin must fall back to
/// re-reading the archive from <c>archive_path</c> (the real full path captured by MAUI's native
/// file picker at connect time) instead of requiring the user to re-pick the file. Mirrors the
/// real-world scenario this fix was built for: the Shapefile provider's archive content is gone on
/// reconnect, but its full path survived.
/// </summary>
public class ShapefileProviderPluginReconnectTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    private static string CreateFixtureZip(string tempDir)
    {
        var point = Factory.CreatePoint(new Coordinate(-39.8, -10.99));
        var features = new List<IFeature> { new Feature(point, new AttributesTable { { "NAME", "Pole" } }) };

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

        return zipPath;
    }

    [Fact]
    public async Task CreateAsync_ArchivePathPointsToRealFile_ReadsItFromDisk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "shp_reconnect_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var zipPath = CreateFixtureZip(tempDir);

            // archive_content absent — exactly what a reconnected Project's config looks like,
            // since ToReconnectValues strips it before persisting.
            var config = new ProviderConfig();
            config.Set("name", "fixture");
            config.Set("archive_path", zipPath);

            var provider = await new ShapefileProviderPlugin().CreateAsync(config, null!);

            provider.GetAll().Single().Properties.Name.Should().Be("Pole");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ArchivePathMissingFromDisk_ThrowsWithPathInMessage()
    {
        var config = new ProviderConfig();
        config.Set("name", "fixture");
        config.Set("archive_path", "/no/such/directory/archive.zip");

        var act = async () => await new ShapefileProviderPlugin().CreateAsync(config, null!);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*archive.zip*");
    }

    [Fact]
    public async Task CreateAsync_NeitherContentNorPath_ThrowsArchiveRequired()
    {
        var config = new ProviderConfig();
        config.Set("name", "fixture");

        var act = async () => await new ShapefileProviderPlugin().CreateAsync(config, null!);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Shapefile ZIP archive is required*");
    }

    [Fact]
    public async Task CreateAsync_ArchiveContentPresent_PrefersContentOverPath()
    {
        // When both are set (shouldn't normally happen, but content must win if it does — it's
        // the freshly-picked data, the path is just a fallback for when content is absent).
        var tempDir = Path.Combine(Path.GetTempPath(), "shp_reconnect_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var zipPath = CreateFixtureZip(tempDir);
            var base64 = Convert.ToBase64String(await File.ReadAllBytesAsync(zipPath));

            var config = new ProviderConfig();
            config.Set("name", "fixture");
            config.Set("archive_content", base64);
            config.Set("archive_path", "/no/such/directory/archive.zip");

            var provider = await new ShapefileProviderPlugin().CreateAsync(config, null!);

            provider.GetAll().Should().HaveCount(1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
