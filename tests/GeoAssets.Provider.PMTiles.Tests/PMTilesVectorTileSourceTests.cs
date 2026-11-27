using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// <see cref="PMTilesVectorTileSource"/> against archives <see cref="PMTilesTestArchive"/> writes in each test: header,
/// gzip-compressed directories and metadata, gzip vector tiles — lookups by Hilbert id through the root directory and
/// through leaf directories, runs, absent tiles and layer selection.
/// </summary>
public sealed class PMTilesVectorTileSourceTests : IDisposable
{
    private static readonly TileLayerSet AllLayers = new("basemap", ["roads", "water"]);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static TileRequest Request(TileAddress tile, TileLayerSet? layers = null) => new(layers ?? AllLayers, tile);

    [Fact]
    public async Task AStoredTile_IsServedAsStored_GzipEncoded()
    {
        var tile = new TileAddress(3, 4, 2);
        var stored = PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads", "water"));
        var path = new PMTilesTestArchive().Add(tile, stored).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var payload = await source.GetVectorTileAsync(Request(tile), CancellationToken.None);

        payload.Content.ToArray().Should().Equal(stored);
        payload.ContentEncoding.Should().Be("gzip");
        payload.ContentType.Should().Be(TileFormats.MapboxVectorTileContentType);
    }

    [Fact]
    public async Task ATileTheArchiveDoesNotHave_IsEmpty()
    {
        var path = new PMTilesTestArchive().Add(new TileAddress(3, 4, 2), PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads"))).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var payload = await source.GetVectorTileAsync(Request(new TileAddress(3, 4, 3)), CancellationToken.None);

        payload.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AZoomOutsideTheArchive_IsEmpty()
    {
        var path = new PMTilesTestArchive { MinZoom = 2, MaxZoom = 4 }
            .Add(new TileAddress(2, 0, 0), PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads")))
            .WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        (await source.GetVectorTileAsync(Request(new TileAddress(5, 0, 0)), CancellationToken.None)).IsEmpty.Should().BeTrue();
        (await source.GetVectorTileAsync(Request(new TileAddress(1, 0, 0)), CancellationToken.None)).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task ARunOfTiles_SharesOneContent()
    {
        var first = new TileAddress(1, 0, 0);
        var stored = PMTilesTestArchive.Gzip(TestVectorTile.Encode("water"));
        var path = new PMTilesTestArchive().Add(first, stored, runLength: 4).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        foreach (var tile in new[] { new TileAddress(1, 0, 0), new TileAddress(1, 0, 1), new TileAddress(1, 1, 1), new TileAddress(1, 1, 0) })
            (await source.GetVectorTileAsync(Request(tile), CancellationToken.None)).Content.ToArray().Should().Equal(stored);

        (await source.GetVectorTileAsync(Request(new TileAddress(2, 0, 0)), CancellationToken.None)).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task TilesBehindLeafDirectories_AreFound()
    {
        var archive = new PMTilesTestArchive { LeafSize = 3 };
        var tiles = new List<(TileAddress Tile, byte[] Stored)>();
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                var tile = new TileAddress(2, x, y);
                var stored = PMTilesTestArchive.Gzip(TestVectorTile.Encode(x % 2 == 0 ? "roads" : "water"));
                tiles.Add((tile, stored));
                archive.Add(tile, stored);
            }
        }

        using var source = new PMTilesVectorTileSource(archive.WriteTo(_directory.Path));

        foreach (var (tile, stored) in tiles)
            (await source.GetVectorTileAsync(Request(tile), CancellationToken.None)).Content.ToArray().Should().Equal(stored, "tile {0}", tile);
    }

    [Fact]
    public async Task UncompressedDirectoriesAndTiles_AreRead()
    {
        var tile = new TileAddress(4, 7, 9);
        var stored = TestVectorTile.Encode("roads");
        var path = new PMTilesTestArchive { InternalCompression = PMTilesCompression.None, TileCompression = PMTilesCompression.None }
            .Add(tile, stored)
            .WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var payload = await source.GetVectorTileAsync(Request(tile), CancellationToken.None);

        payload.Content.ToArray().Should().Equal(stored);
        payload.ContentEncoding.Should().BeNull();
    }

    [Fact]
    public async Task BrotliCompressedDirectoriesAndMetadata_AreRead()
    {
        var tile = new TileAddress(0, 0, 0);
        var stored = TestVectorTile.Encode("roads");
        var path = new PMTilesTestArchive { InternalCompression = PMTilesCompression.Brotli, TileCompression = PMTilesCompression.None }
            .Add(tile, stored)
            .WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        (await source.GetVectorTileAsync(Request(tile), CancellationToken.None)).Content.ToArray().Should().Equal(stored);
    }

    [Fact]
    public async Task ASubsetOfTheLayers_IsCutOutOfTheTile_AndGzippedAgain()
    {
        var tile = new TileAddress(3, 4, 2);
        var path = new PMTilesTestArchive().Add(tile, PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads", "water"))).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var payload = await source.GetVectorTileAsync(Request(tile, new TileLayerSet("water-only", ["water"])), CancellationToken.None);

        payload.ContentEncoding.Should().Be("gzip");
        TestVectorTile.LayerNames(PMTilesTestArchive.Gunzip(payload.Content)).Should().Equal("water");
    }

    [Fact]
    public async Task NoneOfTheRequestedLayers_IsEmpty()
    {
        var tile = new TileAddress(3, 4, 2);
        var path = new PMTilesTestArchive().Add(tile, PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads"))).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var payload = await source.GetVectorTileAsync(Request(tile, new TileLayerSet("water-only", ["water"])), CancellationToken.None);

        payload.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutVectorLayerMetadata_TheTileIsInspected_AndKeptWhole()
    {
        var tile = new TileAddress(3, 4, 2);
        var stored = PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads"));
        var path = new PMTilesTestArchive { MetadataJson = null }.Add(tile, stored).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var payload = await source.GetVectorTileAsync(Request(tile), CancellationToken.None);

        payload.Content.ToArray().Should().Equal(stored);
    }

    [Fact]
    public async Task AnUnknownTileCompression_IsSniffed()
    {
        var tile = new TileAddress(3, 4, 2);
        var stored = PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads", "water"));
        var path = new PMTilesTestArchive { TileCompression = PMTilesCompression.Unknown }.Add(tile, stored).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        (await source.GetVectorTileAsync(Request(tile), CancellationToken.None)).ContentEncoding.Should().Be("gzip");
    }

    [Fact]
    public async Task ARasterArchive_IsRefused()
    {
        var path = new PMTilesTestArchive { TileType = PMTilesTileType.Png }.Add(new TileAddress(0, 0, 0), [1]).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var act = () => source.GetVectorTileAsync(Request(new TileAddress(0, 0, 0)), CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*Png tiles*");
    }

    [Fact]
    public async Task ANonArchive_IsRefused()
    {
        var path = _directory.File("not.pmtiles");
        await File.WriteAllBytesAsync(path, new byte[200]);
        using var source = new PMTilesVectorTileSource(path);

        var act = () => source.GetVectorTileAsync(Request(new TileAddress(0, 0, 0)), CancellationToken.None);

        await act.Should().ThrowAsync<PMTilesFormatException>();
    }

    [Fact]
    public async Task AMissingFile_Throws()
    {
        using var source = new PMTilesVectorTileSource(_directory.File("missing.pmtiles"));

        var act = () => source.GetVectorTileAsync(Request(new TileAddress(0, 0, 0)), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ACancelledRequest_Throws()
    {
        var path = new PMTilesTestArchive().Add(new TileAddress(0, 0, 0), [1]).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => source.GetVectorTileAsync(Request(new TileAddress(0, 0, 0)), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ConcurrentRequests_ShareOneArchive()
    {
        var tile = new TileAddress(3, 4, 2);
        var stored = PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads", "water"));
        var path = new PMTilesTestArchive().Add(tile, stored).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var payloads = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => source.GetVectorTileAsync(Request(tile), CancellationToken.None)));

        payloads.Should().AllSatisfy(p => p.Content.ToArray().Should().Equal(stored));
    }

    // ── DescribeAsync (XD01-459) ──────────────────────────────────────────────

    [Fact]
    public async Task Describe_IsTheMetadatasLayers_TheHeadersZoomsAndBounds_UnderAVersionOfTheFile()
    {
        var path = new PMTilesTestArchive { MinZoom = 2, MaxZoom = 12, Bounds = (-50, -30, -30, -10) }
            .Add(new TileAddress(2, 0, 0), PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads")))
            .WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var description = await source.DescribeAsync(CancellationToken.None);

        description.LayerIds.Should().Equal("roads", "water");
        (description.MinZoom, description.MaxZoom).Should().Be((2, 12));
        description.Bounds.Should().Be(new BoundingBox(-50, -30, -30, -10));
        var file = new FileInfo(path);
        description.Version.Should().Be(PMTilesVectorTileSource.FileVersion(file.Length, file.LastWriteTimeUtc.Ticks));
        description.Version.Should().MatchRegex("^[0-9a-f]{16}$");
        (await source.DescribeAsync(CancellationToken.None)).Should().BeSameAs(description, "it is read once, when the archive is opened");
    }

    [Fact]
    public void TheFileVersion_ChangesWithTheFilesLengthOrModificationTime()
    {
        var version = PMTilesVectorTileSource.FileVersion(1000, 638_000_000_000_000_000);

        PMTilesVectorTileSource.FileVersion(1000, 638_000_000_000_000_000).Should().Be(version);
        PMTilesVectorTileSource.FileVersion(1001, 638_000_000_000_000_000).Should().NotBe(version);
        PMTilesVectorTileSource.FileVersion(1000, 638_000_000_000_000_001).Should().NotBe(version);
    }

    [Fact]
    public async Task Describe_LeavesOutLayersATileRequestCannotCarry_ClampsTheZooms_AndDropsBoundsThatAreNoExtent()
    {
        var path = new PMTilesTestArchive
            {
                MetadataJson = """{"vector_layers":[{"id":"roads"},{"id":"bad name"},{"id":"roads"}]}""",
                MinZoom = 30,
                MaxZoom = 40,
                Bounds = (10, 0, -10, 5),
            }
            .Add(new TileAddress(0, 0, 0), PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads")))
            .WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var description = await source.DescribeAsync(CancellationToken.None);

        description.LayerIds.Should().Equal("roads");
        (description.MinZoom, description.MaxZoom).Should().Be((TileAddress.MaxZoom, TileAddress.MaxZoom));
        description.Bounds.Should().BeNull();
    }

    [Fact]
    public async Task Describe_AnArchiveNamingNoUsableLayer_FailsWithAFormatError()
    {
        var path = new PMTilesTestArchive { MetadataJson = """{"vector_layers":[{"id":"bad name"}]}""" }.Add(new TileAddress(0, 0, 0), PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads"))).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var describe = () => source.DescribeAsync(CancellationToken.None);

        await describe.Should().ThrowAsync<PMTilesFormatException>().WithMessage("*names no vector layer*");
    }

    [Fact]
    public async Task Describe_HonoursCancellation()
    {
        var path = new PMTilesTestArchive().Add(new TileAddress(0, 0, 0), PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads"))).WriteTo(_directory.Path);
        using var source = new PMTilesVectorTileSource(path);

        var describe = () => source.DescribeAsync(new CancellationToken(canceled: true));

        await describe.Should().ThrowAsync<OperationCanceledException>();
    }
}
