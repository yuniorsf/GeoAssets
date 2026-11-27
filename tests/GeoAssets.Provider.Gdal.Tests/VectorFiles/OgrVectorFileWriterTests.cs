using System.Globalization;
using System.Runtime.CompilerServices;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Provider.Gdal.VectorFiles;
using NetTopologySuite.Geometries;
using OSGeo.OGR;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests.VectorFiles;

/// <summary>
/// <see cref="OgrVectorFileWriter"/> (XD01-466): every file it writes is read back with <see cref="OgrVectorFileReader"/>.
/// Native: these run where the plugin ships a GDAL runtime (macOS, Linux), like the reader's tests.
/// </summary>
public sealed class OgrVectorFileWriterTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly OgrVectorFileWriter _writer = new();
    private readonly OgrVectorFileReader _reader = new();

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData(VectorFileFormat.FlatGeobuf)]
    [InlineData(VectorFileFormat.GeoParquet)]
    public void TheShippedRuntime_CanWriteTheFormat(VectorFileFormat format)
    {
        _writer.IsAvailable(format).Should().BeTrue("the MaxRev Minimal runtime's {0} driver creates files", OgrVectorFileReader.DriverName(format));
    }

    [Theory]
    [InlineData(VectorFileFormat.FlatGeobuf)]
    [InlineData(VectorFileFormat.GeoParquet)]
    public async Task AWrittenFile_ReadsBack_WithItsGeometryIdNameAndAttributes(VectorFileFormat format)
    {
        var written = MixedFeatures(format);
        var path = Output(format);

        var result = await _writer.WriteAsync(path, new VectorFileWriteRequest(format), Stream(written), CancellationToken.None);

        result.FeaturesWritten.Should().Be(written.Count);
        result.Attributes.Should().Equal("kind", "voltage", "owner");
        AssertSame(written, ReadAll(path, format));
    }

    [Theory]
    [InlineData(VectorFileFormat.FlatGeobuf)]
    [InlineData(VectorFileFormat.GeoParquet)]
    public async Task OntoAStream_TheFileReadsBackTheSame(VectorFileFormat format)
    {
        var written = MixedFeatures(format);
        using var destination = new MemoryStream();

        await _writer.WriteAsync(destination, new VectorFileWriteRequest(format), Stream(written), CancellationToken.None);

        destination.CanWrite.Should().BeTrue("the caller's stream is left open");
        var path = Output(format);
        await File.WriteAllBytesAsync(path, destination.ToArray());
        AssertSame(written, ReadAll(path, format));
    }

    [Theory]
    [InlineData(VectorFileFormat.FlatGeobuf, false)]
    [InlineData(VectorFileFormat.GeoParquet, false)]
    [InlineData(VectorFileFormat.FlatGeobuf, true)]
    [InlineData(VectorFileFormat.GeoParquet, true)]
    public async Task MoreFeaturesThanABatch_AreAllWritten(VectorFileFormat format, bool declareAttributes)
    {
        const int count = 2_537;
        var path = Output(format);
        var request = new VectorFileWriteRequest(format)
        {
            BatchSize = 1_000,
            Attributes = declareAttributes ? ["kind"] : null,
        };

        var result = await _writer.WriteAsync(path, request, Generate(count), CancellationToken.None);

        result.FeaturesWritten.Should().Be(count);
        var pages = new List<VectorFilePage>();
        long? offset = 0;
        while (offset is { } next)
        {
            var page = _reader.ReadPage(new VectorFileReadRequest(path, format, next, 1_000), CancellationToken.None);
            pages.Add(page);
            offset = page.NextOffset;
        }

        pages.Should().HaveCount(3);
        var read = pages.SelectMany(p => p.Features).ToList();
        read.Select(f => f.Id).Order(StringComparer.Ordinal).Should()
            .Equal(Enumerable.Range(0, count).Select(i => $"f-{i}").Order(StringComparer.Ordinal), "every feature is written once, whatever order the spatial index sorts them in");
        foreach (var feature in read)
            feature.Properties.CustomAttributes["kind"].Should().Be(Kind(int.Parse(feature.Id.AsSpan(2), CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(VectorFileFormat.FlatGeobuf, false)]
    [InlineData(VectorFileFormat.GeoParquet, false)]
    [InlineData(VectorFileFormat.FlatGeobuf, true)]
    [InlineData(VectorFileFormat.GeoParquet, true)]
    public async Task AnEndlessInput_IsStreamed_UntilCancelled_LeavingNoFile(VectorFileFormat format, bool declareAttributes)
    {
        using var cancellation = new CancellationTokenSource();
        var yielded = 0;
        var path = Output(format);
        var request = new VectorFileWriteRequest(format) { Attributes = declareAttributes ? ["kind"] : null };

        var act = () => _writer.WriteAsync(path, request, Endless(cancellation, cancelAfter: 1_500, onYield: () => yielded++), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        yielded.Should().Be(1_500, "the writer pulls features one at a time instead of materializing its input");
        Directory.EnumerateFileSystemEntries(_directory.Path).Should().BeEmpty("a cancelled write leaves neither the file nor its temporaries");
    }

    [Fact]
    public async Task AnAlreadyCancelledToken_NeverEnumeratesTheInput()
    {
        var enumerated = false;

        var act = () => _writer.WriteAsync(
            Output(VectorFileFormat.FlatGeobuf), new VectorFileWriteRequest(VectorFileFormat.FlatGeobuf), Observed(() => enumerated = true),
            new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        enumerated.Should().BeFalse();
    }

    [Fact]
    public async Task AFailedWrite_LeavesAnExistingFileUntouched()
    {
        var path = Output(VectorFileFormat.GeoParquet);
        await File.WriteAllTextAsync(path, "previous export");
        var clashing = new[] { Feature("a", attributes: [("Kind", "1")]), Feature("b", attributes: [("kind", "2")]) };

        var act = () => _writer.WriteAsync(path, new VectorFileWriteRequest(VectorFileFormat.GeoParquet), Stream(clashing), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*'Kind' and 'kind' differ only by case*");
        (await File.ReadAllTextAsync(path)).Should().Be("previous export");
        Directory.EnumerateFiles(_directory.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task ASuccessfulWrite_ReplacesAnExistingFile()
    {
        var path = Output(VectorFileFormat.FlatGeobuf);
        await File.WriteAllTextAsync(path, "previous export");

        await _writer.WriteAsync(path, new VectorFileWriteRequest(VectorFileFormat.FlatGeobuf), Stream([Feature("only")]), CancellationToken.None);

        ReadAll(path, VectorFileFormat.FlatGeobuf).Select(f => f.Id).Should().Equal("only");
        Directory.EnumerateFiles(_directory.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task DeclaredAttributes_AreTheOnlyColumns()
    {
        var path = Output(VectorFileFormat.GeoParquet);
        var request = new VectorFileWriteRequest(VectorFileFormat.GeoParquet) { Attributes = ["owner", "kind"] };

        var result = await _writer.WriteAsync(path, request, Stream(MixedFeatures(VectorFileFormat.GeoParquet)), CancellationToken.None);

        result.Attributes.Should().Equal("owner", "kind");
        var read = _reader.ReadPage(new VectorFileReadRequest(path, VectorFileFormat.GeoParquet) { LayerName = "export" }, CancellationToken.None).Features;
        read.SelectMany(f => f.Properties.CustomAttributes.Keys).Distinct(StringComparer.Ordinal).Should().BeSubsetOf(["id", "name", "owner", "kind"]);
        read.Single(f => string.Equals(f.Id, "pole-1", StringComparison.Ordinal)).Properties.CustomAttributes.Should().NotContainKey("voltage");
    }

    [Fact]
    public async Task IdAndNameAttributes_AreLeftToTheFeaturesOwnColumns()
    {
        var path = Output(VectorFileFormat.FlatGeobuf);
        var feature = Feature("real-id", name: "Real name", attributes: [("ID", "stale-id"), ("Name", "stale name"), ("kind", "1")]);

        var result = await _writer.WriteAsync(path, new VectorFileWriteRequest(VectorFileFormat.FlatGeobuf), Stream([feature]), CancellationToken.None);

        result.Attributes.Should().Equal("kind");
        var read = ReadAll(path, VectorFileFormat.FlatGeobuf).Single();
        read.Id.Should().Be("real-id");
        read.Properties.Name.Should().Be("Real name");
    }

    [Fact]
    public async Task AFlatGeobufFeatureWithoutGeometry_FailsTheWrite_LeavingNoFile()
    {
        var path = Output(VectorFileFormat.FlatGeobuf);
        var features = new[] { Feature("a"), Feature("ghost-1", null, "Sem geometria") };

        var act = () => _writer.WriteAsync(path, new VectorFileWriteRequest(VectorFileFormat.FlatGeobuf), Stream(features), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*'ghost-1' has no geometry*spatial index*");
        Directory.EnumerateFileSystemEntries(_directory.Path).Should().BeEmpty();
    }

    [Fact]
    public Task AMissingFolder_ThrowsDirectoryNotFound()
    {
        var act = () => _writer.WriteAsync(
            Path.Combine(_directory.Path, "missing", "out.fgb"), new VectorFileWriteRequest(VectorFileFormat.FlatGeobuf), Stream([Feature("a")]),
            CancellationToken.None);

        return act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task AMissingDriver_IsATypedErrorAtUse()
    {
        using var driver = Ogr.GetDriverByName("Parquet");
        driver.Deregister();
        try
        {
            _writer.IsAvailable(VectorFileFormat.GeoParquet).Should().BeFalse();
            var act = () => _writer.WriteAsync(
                Output(VectorFileFormat.GeoParquet), new VectorFileWriteRequest(VectorFileFormat.GeoParquet), Stream([Feature("a")]),
                CancellationToken.None);

            (await act.Should().ThrowAsync<VectorFileFormatUnavailableException>())
                .Where(ex => ex.Format == VectorFileFormat.GeoParquet)
                .WithMessage("*'Parquet' driver*");
        }
        finally
        {
            driver.Register();
        }
    }

    private string Output(VectorFileFormat format) => _directory.File("export" + OgrVectorFileWriter.Extension(format));

    private List<GeoFeature> ReadAll(string path, VectorFileFormat format) =>
        [.. _reader.ReadPage(new VectorFileReadRequest(path, format, 0, VectorFileReadRequest.MaxLimit), CancellationToken.None).Features];

    private static void AssertSame(IReadOnlyList<GeoFeature> written, IReadOnlyList<GeoFeature> read)
    {
        read.Select(f => f.Id).Should().BeEquivalentTo(written.Select(f => f.Id));
        foreach (var expected in written)
        {
            var actual = read.Single(f => string.Equals(f.Id, expected.Id, StringComparison.Ordinal));
            actual.Properties.Name.Should().Be(expected.Properties.Name);
            foreach (var (key, value) in expected.Properties.CustomAttributes)
                actual.Properties.CustomAttributes.Should().Contain(key, value);
            actual.Properties.CustomAttributes.Keys.Except(expected.Properties.CustomAttributes.Keys, StringComparer.Ordinal).Should().BeSubsetOf(["id", "name"]);
            if (expected.Geometry is null)
            {
                actual.Geometry.Should().BeNull();
                continue;
            }

            actual.Geometry!.NtsGeometry.EqualsExact(expected.Geometry.NtsGeometry, 1e-9)
                .Should().BeTrue("{0} keeps its geometry, longitude first: expected {1}, read {2}", expected.Id, expected.Geometry.NtsGeometry, actual.Geometry.NtsGeometry);
            actual.Geometry.Srid.Should().Be(4326);
        }
    }

    /// <summary>A point, a line, a polygon and a multipolygon with sparse attributes — and, in GeoParquet, a feature without geometry.</summary>
    private static List<GeoFeature> MixedFeatures(VectorFileFormat format)
    {
        var multi = GeoFactory.Wgs84.CreateMultiPolygon(
        [
            GeoFactory.Wgs84.CreatePolygon([new(-40, -10), new(-39, -10), new(-39, -9), new(-40, -10)]),
            GeoFactory.Wgs84.CreatePolygon([new(-30, -5), new(-29, -5), new(-29, -4), new(-30, -5)]),
        ]);
        List<GeoFeature> features =
        [
            Feature("pole-1", new GeoPoint(-38.5108, -12.9711), "Poste 1", ("kind", "pole"), ("voltage", "13.8")),
            Feature("line-1", new GeoLineString([(-38.5, -12.9), (-38.4, -12.8), (-38.3, -12.85)]), "MT 100", ("kind", "mv"), ("owner", "coelba")),
            Feature("area-1", new GeoPolygon([(-38.6, -13.0), (-38.5, -13.0), (-38.5, -12.9), (-38.6, -13.0)]), "Bairro", ("owner", "pmsalvador")),
            Feature("multi-1", GeoGeometry.FromNts(multi), "Ilhas"),
        ];
        if (format == VectorFileFormat.GeoParquet)
            features.Add(Feature("ghost-1", null, "Sem geometria", ("kind", "ghost")));
        return features;
    }

    private static GeoFeature Feature(string id, GeoGeometry? geometry, string name, params (string Key, string Value)[] attributes)
    {
        var feature = new GeoFeature { Id = id, Geometry = geometry, Properties = new GeoFeatureProperties { Name = name } };
        foreach (var (key, value) in attributes)
            feature.Properties.CustomAttributes[key] = value;
        return feature;
    }

    private static GeoFeature Feature(string id, string name = "", params (string Key, string Value)[] attributes) =>
        Feature(id, new GeoPoint(-38.5, -12.9), name, attributes);

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
            yield return Feature($"f-{i}", new GeoPoint(-40 + (i * 0.001), -10 - (i * 0.0005)), $"F{i}", ("kind", Kind(i)));
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
            yield return Feature($"f-{i}", new GeoPoint(i % 180, 0), $"F{i}", ("kind", "x"));
        }
    }

    private static async IAsyncEnumerable<GeoFeature> Observed(Action onEnumerated)
    {
        onEnumerated();
        await Task.Yield();
        yield return Feature("a");
    }
}
