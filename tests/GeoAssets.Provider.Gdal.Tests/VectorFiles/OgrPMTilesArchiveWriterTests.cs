using System.Globalization;
using System.Runtime.CompilerServices;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Provider.Gdal.VectorFiles;
using GeoAssets.Provider.PMTiles;
using GeoAssets.Provider.PMTiles.Mvt;
using GeoAssets.Provider.PMTiles.Packs;
using OSGeo.OGR;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests.VectorFiles;

/// <summary>
/// <see cref="OgrPMTilesArchiveWriter"/> (XD01-472): every archive it writes is read back with the GDAL-free PMTiles reader
/// (<see cref="PMTilesArchive"/>, <see cref="PMTilesVectorTileSource"/>, the offline pack storage and provider). Native:
/// these run where the plugin ships a GDAL runtime (macOS, Linux), like the other writer's tests.
/// </summary>
public sealed class OgrPMTilesArchiveWriterTests : IDisposable
{
    private const string PoleType = "11111111-1111-1111-1111-111111111111";
    private const string NetworkType = "22222222-2222-2222-2222-222222222222";

    private static readonly Dictionary<string, string> Layers = new(StringComparer.Ordinal) { [PoleType] = "Pole", [NetworkType] = "Network" };

    private readonly TempDirectory _directory = new();
    private readonly OgrPMTilesArchiveWriter _writer = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void TheShippedRuntime_CanWritePMTiles()
    {
        _writer.IsAvailable.Should().BeTrue("the MaxRev Minimal runtime's PMTiles driver creates archives");
    }

    [Fact]
    public async Task AnExportedArchive_HoldsEveryFeature_WithItsIdNameAndAttributes_AtEveryRequestedZoom()
    {
        var written = NetworkFeatures();
        var path = Output();
        var request = new VectorTileArchiveWriteRequest(2, 6) { LayerNames = Layers };

        var result = await _writer.WriteAsync(path, request, Stream(written), CancellationToken.None);

        result.Should().BeEquivalentTo(new VectorTileArchiveWriteResult(3, 0, ["Pole", "Network"], 2, 6));
        using var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None);
        archive.Header.Version.Should().Be(PMTilesHeader.SupportedVersion);
        archive.Header.TileType.Should().Be(PMTilesTileType.Mvt);
        archive.Header.MinZoom.Should().Be(2);
        archive.Header.MaxZoom.Should().Be(6);
        archive.VectorLayerIds.Should().BeEquivalentTo("Pole", "Network");
        for (var zoom = 2; zoom <= 6; zoom++)
        {
            for (var i = 0; i < written.Count; i++)
            {
                var expected = written[i];
                var anchor = expected.Geometry!.NtsGeometry.Coordinates[0];
                var tile = TileAddress.Containing(anchor.X, anchor.Y, zoom);
                var layer = (await DecodeAsync(archive, tile)).Single(l => string.Equals(l.Name, request.LayerNameOf(expected.Properties.AssetTypeId), StringComparison.Ordinal));
                var feature = layer.Features.Should().ContainSingle(f => f.Id == (ulong)(i + 1), "{0} is at position {1} of the input, at zoom {2}", expected.Id, i + 1, zoom).Subject;
                feature.Properties.Should().Contain("id", expected.Id).And.Contain("name", expected.Properties.Name);
                foreach (var (key, value) in expected.Properties.CustomAttributes)
                    feature.Properties.Should().Contain(key, value);
            }
        }

        (await archive.GetTileAsync(new TileAddress(1, 0, 0), CancellationToken.None)).Should().BeNull("zoom 1 wasn't asked for");
        (await archive.GetTileAsync(TileAddress.Containing(-38.5, -12.9, 7), CancellationToken.None)).Should().BeNull("zoom 7 wasn't asked for");
    }

    [Fact]
    public async Task AFeatureCutAcrossTiles_CarriesTheSameMvtId_InEveryPiece()
    {
        var path = Output();
        var line = Feature("mv-1", new GeoLineString([(-40, -12), (-36, -13)]), "MT 1", NetworkType);

        await _writer.WriteAsync(path, new VectorTileArchiveWriteRequest(6, 6) { LayerNames = Layers }, Stream([Feature("p", new GeoPoint(-38.5, -12.9), "P", PoleType), line]), CancellationToken.None);

        using var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None);
        var west = TileAddress.Containing(-40, -12, 6);
        var east = TileAddress.Containing(-36, -13, 6);
        west.Should().NotBe(east, "the line crosses a tile seam at zoom 6");
        foreach (var tile in new[] { west, east })
        {
            var piece = (await DecodeAsync(archive, tile)).Single(l => string.Equals(l.Name, "Network", StringComparison.Ordinal)).Features.Should().ContainSingle().Subject;
            piece.Id.Should().Be(2UL, "the line is the input's second feature, in tile {0}", tile);
            piece.Properties.Should().Contain("id", "mv-1");
        }
    }

    [Fact]
    public async Task TheArchive_IsServedByPMTilesVectorTileSource_AndAcceptedAsAnOfflinePack_WithItsFeaturesJoinedByMvtId()
    {
        var path = Output();
        await _writer.WriteAsync(path, new VectorTileArchiveWriteRequest(4, 8) { LayerNames = Layers }, Stream(NetworkFeatures()), CancellationToken.None);

        using (var source = new PMTilesVectorTileSource(path))
        {
            var payload = await source.GetVectorTileAsync(
                new TileRequest(new TileLayerSet("export", ["Pole", "Network"]), TileAddress.Containing(-38.5108, -12.9711, 8)), CancellationToken.None);
            payload.IsEmpty.Should().BeFalse();
            payload.ContentType.Should().Be(TileFormats.MapboxVectorTileContentType);
        }

        var storage = new PMTilesPackStorage(_directory.File("packs"));
        using var pack = await storage.ImportAsync(path, CancellationToken.None);
        pack.Header.MinZoom.Should().Be(4);
        pack.Header.MaxZoom.Should().Be(8);
        using var provider = new PMTilesPackAssetProvider(pack);
        var read = await provider.ReadAsync(new BoundingBox(-41, -14, -35, -11), mapZoom: 8, CancellationToken.None);

        read.Should().HaveCount(3, "each feature is answered once, its pieces joined by their MVT id");
        read.Select(f => f.Properties.CustomAttributes["id"]).Should().BeEquivalentTo("pole-1", "mv-1", "area-1");
        read.Should().OnlyContain(f => f.Id.StartsWith($"pmtiles:{pack.Id}:7:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OntoAStream_TheArchiveReadsBackTheSame()
    {
        using var destination = new MemoryStream();

        var result = await _writer.WriteAsync(destination, new VectorTileArchiveWriteRequest(0, 3), Stream(NetworkFeatures()), CancellationToken.None);

        destination.CanWrite.Should().BeTrue("the caller's stream is left open");
        result.Layers.Should().Equal(PoleType, NetworkType);
        var path = Output();
        await File.WriteAllBytesAsync(path, destination.ToArray());
        using var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None);
        var layers = await DecodeAsync(archive, new TileAddress(0, 0, 0));
        layers.Select(l => l.Name).Should().BeEquivalentTo(PoleType, NetworkType);
        layers.SelectMany(l => l.Features).Select(f => f.Properties["id"]).Should().BeEquivalentTo("pole-1", "mv-1", "area-1");
    }

    [Fact]
    public async Task MoreFeaturesThanABatch_AreAllWritten_EachWithItsOwnMvtId()
    {
        const int count = 2_537;
        var path = Output();

        var result = await _writer.WriteAsync(path, new VectorTileArchiveWriteRequest(0, 2), Generate(count), CancellationToken.None);

        result.FeaturesWritten.Should().Be(count);
        using var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None);
        var features = (await DecodeAsync(archive, new TileAddress(0, 0, 0))).Single().Features;
        features.Should().HaveCount(count);
        features.Select(f => f.Id!.Value).Should().BeEquivalentTo(Enumerable.Range(1, count).Select(i => (ulong)i));
        foreach (var feature in features)
        {
            var index = (int)feature.Id!.Value - 1;
            feature.Properties["id"].Should().Be($"f-{index}");
            feature.Properties["kind"].Should().Be(Kind(index));
        }
    }

    [Fact]
    public async Task AnEndlessInput_IsStreamed_UntilCancelled_LeavingNoFile()
    {
        using var cancellation = new CancellationTokenSource();
        var yielded = 0;

        var act = () => _writer.WriteAsync(
            Output(), new VectorTileArchiveWriteRequest(0, 4), Endless(cancellation, cancelAfter: 1_500, onYield: () => yielded++), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        yielded.Should().Be(1_500, "the writer pulls features one at a time instead of materializing its input");
        Directory.EnumerateFileSystemEntries(_directory.Path).Should().BeEmpty("a cancelled write leaves neither the archive nor the driver's temporaries");
    }

    [Fact]
    public async Task AnAlreadyCancelledToken_NeverEnumeratesTheInput()
    {
        var enumerated = false;

        var act = () => _writer.WriteAsync(Output(), new VectorTileArchiveWriteRequest(), Observed(() => enumerated = true), new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        enumerated.Should().BeFalse();
    }

    [Fact]
    public async Task AFailedWrite_LeavesAnExistingArchiveUntouched()
    {
        var path = Output();
        await File.WriteAllTextAsync(path, "previous export");
        var clashing = new[] { Feature("a", attributes: [("Kind", "1")]), Feature("b", attributes: [("kind", "2")]) };

        var act = () => _writer.WriteAsync(path, new VectorTileArchiveWriteRequest(0, 2), Stream(clashing), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*'Kind' and 'kind'*differ only by case*");
        (await File.ReadAllTextAsync(path)).Should().Be("previous export");
        Directory.EnumerateFiles(_directory.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task ASuccessfulWrite_ReplacesAnExistingArchive()
    {
        var path = Output();
        await File.WriteAllTextAsync(path, "previous export");

        await _writer.WriteAsync(path, new VectorTileArchiveWriteRequest(0, 1), Stream([Feature("only")]), CancellationToken.None);

        using var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None);
        (await DecodeAsync(archive, new TileAddress(0, 0, 0))).Single().Features.Single().Properties["id"].Should().Be("only");
        Directory.EnumerateFiles(_directory.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task FeaturesWithoutGeometry_AreSkipped_AndCounted_KeepingTheOthersPositions()
    {
        var path = Output();
        var features = new[] { new GeoFeature { Id = "ghost", Properties = new GeoFeatureProperties { Name = "Sem geometria" } }, Feature("real") };

        var result = await _writer.WriteAsync(path, new VectorTileArchiveWriteRequest(0, 0), Stream(features), CancellationToken.None);

        result.FeaturesWritten.Should().Be(1);
        result.FeaturesSkipped.Should().Be(1);
        using var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None);
        var feature = (await DecodeAsync(archive, new TileAddress(0, 0, 0))).Single().Features.Single();
        feature.Id.Should().Be(2UL, "the MVT id is the feature's position in the input");
        feature.Properties["id"].Should().Be("real");
    }

    [Fact]
    public async Task AnInputWithNothingToTile_FailsTheWrite_LeavingNoFile()
    {
        var empty = () => _writer.WriteAsync(Output(), new VectorTileArchiveWriteRequest(0, 2), Stream([]), CancellationToken.None);
        var geometryless = () => _writer.WriteAsync(
            Output(), new VectorTileArchiveWriteRequest(0, 2), Stream([new GeoFeature { Id = "ghost" }]), CancellationToken.None);

        await empty.Should().ThrowAsync<InvalidDataException>().WithMessage("There are no features to tile*");
        await geometryless.Should().ThrowAsync<InvalidDataException>().WithMessage("None of the 1 features has a geometry*");
        Directory.EnumerateFileSystemEntries(_directory.Path).Should().BeEmpty();
    }

    [Fact]
    public async Task DeclaredAttributes_AreTheOnlyTags_AndIdAndNameStayTheFeaturesOwn()
    {
        var path = Output();
        var feature = Feature("real-id", "Real name", ("ID", "stale-id"), ("Name", "stale"), ("kind", "pole"), ("voltage", "13.8"));

        await _writer.WriteAsync(path, new VectorTileArchiveWriteRequest(0, 0) { Attributes = ["kind", "id"] }, Stream([feature]), CancellationToken.None);

        using var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None);
        var read = (await DecodeAsync(archive, new TileAddress(0, 0, 0))).Single().Features.Single();
        read.Properties.Should().BeEquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = "real-id", ["name"] = "Real name", ["kind"] = "pole" });
    }

    [Fact]
    public async Task TheRequestsTileGeometry_IsTheArchives()
    {
        var path = Output();
        var request = new VectorTileArchiveWriteRequest(0, 1) { Extent = 512, Buffer = 8, Simplification = 1, SimplificationMaxZoom = 0.5 };

        await _writer.WriteAsync(path, request, Stream(NetworkFeatures()), CancellationToken.None);

        using var archive = await PMTilesArchive.OpenAsync(path, CancellationToken.None);
        (await DecodeAsync(archive, new TileAddress(1, 0, 1))).Should().OnlyContain(l => l.Extent == 512);
    }

    [Fact]
    public Task AMissingFolder_ThrowsDirectoryNotFound()
    {
        var act = () => _writer.WriteAsync(
            Path.Combine(_directory.Path, "missing", "out.pmtiles"), new VectorTileArchiveWriteRequest(), Stream([Feature("a")]), CancellationToken.None);

        return act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task AMissingDriver_IsATypedErrorAtUse()
    {
        using var driver = Ogr.GetDriverByName(OgrPMTilesArchiveWriter.DriverName);
        driver.Deregister();
        try
        {
            _writer.IsAvailable.Should().BeFalse();
            var act = () => _writer.WriteAsync(Output(), new VectorTileArchiveWriteRequest(), Stream([Feature("a")]), CancellationToken.None);

            await act.Should().ThrowAsync<VectorTileArchiveUnavailableException>().WithMessage("*'PMTiles' driver*");
        }
        finally
        {
            driver.Register();
        }
    }

    private string Output() => _directory.File("export" + OgrPMTilesArchiveWriter.Extension);

    private static async Task<IReadOnlyList<MvtLayer>> DecodeAsync(PMTilesArchive archive, TileAddress tile)
    {
        var stored = await archive.GetTileAsync(tile, CancellationToken.None);
        stored.Should().NotBeNull("the archive has tile {0}", tile);
        var bytes = PMTilesCodec.Decompress(stored, PMTilesCodec.Resolve(archive.Header.TileCompression, stored), PMTilesArchive.MaxTileBytes);
        return MvtTileDecoder.Decode(bytes, int.MaxValue);
    }

    /// <summary>A pole, a medium-voltage line crossing tile seams and a polygon, in two asset types.</summary>
    private static List<GeoFeature> NetworkFeatures() =>
    [
        Feature("pole-1", new GeoPoint(-38.5108, -12.9711), "Poste 1", PoleType, ("kind", "pole"), ("voltage", "13.8")),
        Feature("mv-1", new GeoLineString([(-38.9, -12.6), (-38.4, -12.8), (-37.9, -13.1)]), "MT 100", NetworkType, ("kind", "mv"), ("owner", "coelba")),
        Feature("area-1", new GeoPolygon([(-38.6, -13.0), (-38.5, -13.0), (-38.5, -12.9), (-38.6, -13.0)]), "Bairro", NetworkType, ("owner", "pmsalvador")),
    ];

    private static GeoFeature Feature(string id, GeoGeometry? geometry, string name, string assetType, params (string Key, string Value)[] attributes)
    {
        var feature = new GeoFeature { Id = id, Geometry = geometry, Properties = new GeoFeatureProperties { Name = name, AssetTypeId = assetType } };
        foreach (var (key, value) in attributes)
            feature.Properties.CustomAttributes[key] = value;
        return feature;
    }

    private static GeoFeature Feature(string id, string name = "", params (string Key, string Value)[] attributes) =>
        Feature(id, new GeoPoint(-38.5, -12.9), name, PoleType, attributes);

    private static async IAsyncEnumerable<GeoFeature> Stream(IEnumerable<GeoFeature> features)
    {
        foreach (var feature in features)
        {
            await Task.Yield();
            yield return feature;
        }
    }

    private static async IAsyncEnumerable<GeoFeature> Generate(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i % 100 == 0)
                await Task.Yield();
            yield return Feature($"f-{i}", new GeoPoint(-40 + (i * 0.001), -10 - (i * 0.0005)), $"F{i}", PoleType, ("kind", Kind(i)));
        }
    }

    private static string Kind(int i) => (i % 7).ToString(CultureInfo.InvariantCulture);

    private static async IAsyncEnumerable<GeoFeature> Endless(
        CancellationTokenSource cancellation, int cancelAfter, Action onYield, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; ; i++)
        {
            if (i == cancelAfter)
                await cancellation.CancelAsync();
            await Task.Yield();
            if (i < cancelAfter)
                onYield();
            yield return Feature($"f-{i}", new GeoPoint((i % 360) - 179.5, 0), $"F{i}", PoleType, ("kind", "x"));
        }
    }

    private static async IAsyncEnumerable<GeoFeature> Observed(Action onEnumerated)
    {
        onEnumerated();
        await Task.Yield();
        yield return Feature("a");
    }
}
