using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Provider.Gdal.VectorFiles;
using OSGeo.OGR;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests.VectorFiles;

/// <summary>
/// <see cref="OgrVectorFileReader"/> against FlatGeobuf and GeoParquet files OGR writes in each test. The shipped MaxRev
/// Minimal runtime (GDAL 3.13) has both the <c>FlatGeobuf</c> and the <c>Parquet</c> drivers; the driver-missing path
/// is exercised by deregistering a driver for the length of one test.
/// </summary>
public sealed class OgrVectorFileReaderTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly OgrVectorFileReader _reader = new();

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData(VectorFileFormat.FlatGeobuf)]
    [InlineData(VectorFileFormat.GeoParquet)]
    public void TheShippedRuntime_HasTheDriver(VectorFileFormat format)
    {
        _reader.IsAvailable(format).Should().BeTrue("the MaxRev Minimal runtime includes OGR's {0} driver", OgrVectorFileReader.DriverName(format));
    }

    [Theory]
    [InlineData(VectorFileFormat.FlatGeobuf)]
    [InlineData(VectorFileFormat.GeoParquet)]
    public void ReadPage_PagesThroughTheFile(VectorFileFormat format)
    {
        var path = TestVectorFiles.Write(_directory.Path, format, 25);

        var first = _reader.ReadPage(new VectorFileReadRequest(path, format, 0, 10), CancellationToken.None);
        var second = _reader.ReadPage(new VectorFileReadRequest(path, format, first.NextOffset!.Value, 10), CancellationToken.None);
        var last = _reader.ReadPage(new VectorFileReadRequest(path, format, second.NextOffset!.Value, 10), CancellationToken.None);

        first.Features.Select(f => f.Properties.Name).Should().Equal(Enumerable.Range(0, 10).Select(i => $"P{i}"));
        first.NextOffset.Should().Be(10);
        first.TotalCount.Should().Be(25);
        second.Features.Select(f => f.Properties.Name).Should().Equal(Enumerable.Range(10, 10).Select(i => $"P{i}"));
        last.Features.Should().HaveCount(5);
        last.NextOffset.Should().BeNull();
    }

    [Theory]
    [InlineData(VectorFileFormat.FlatGeobuf)]
    [InlineData(VectorFileFormat.GeoParquet)]
    public void ReadPage_MapsGeometryIdAndAttributes(VectorFileFormat format)
    {
        var path = TestVectorFiles.Write(_directory.Path, format, 3);

        var feature = _reader.ReadPage(new VectorFileReadRequest(path, format), CancellationToken.None).Features[2];

        feature.Id.Should().Be("pole-2");
        feature.Properties.Name.Should().Be("P2");
        feature.Properties.CustomAttributes.Should().Contain("kind", "2").And.Contain("id", "pole-2");
        var point = feature.Geometry.Should().BeOfType<GeoPoint>().Subject;
        point.Coordinates[0].Should().BeApproximately(2, 1e-9);
        point.Coordinates[1].Should().BeApproximately(-2, 1e-9);
    }

    [Fact]
    public void ReadPage_OverASpatiallyIndexedFile_CoversEveryFeatureOnce()
    {
        var path = TestVectorFiles.Write(_directory.Path, VectorFileFormat.FlatGeobuf, 25, spatialIndex: true);
        var names = new List<string>();

        long? offset = 0;
        while (offset is { } next)
        {
            var page = _reader.ReadPage(new VectorFileReadRequest(path, VectorFileFormat.FlatGeobuf, next, 7), CancellationToken.None);
            page.Features.Count.Should().BeLessThanOrEqualTo(7);
            names.AddRange(page.Features.Select(f => f.Properties.Name));
            offset = page.NextOffset;
        }

        names.Should().BeEquivalentTo(Enumerable.Range(0, 25).Select(i => $"P{i}"));
    }

    [Fact]
    public void ReadPage_ReprojectsToWgs84_LongitudeFirst()
    {
        var path = TestVectorFiles.Write(_directory.Path, VectorFileFormat.FlatGeobuf, 1, epsg: 31983);

        var point = (GeoPoint)_reader.ReadPage(new VectorFileReadRequest(path, VectorFileFormat.FlatGeobuf), CancellationToken.None).Features[0].Geometry!;

        point.Coordinates[0].Should().BeApproximately(-45, 0.01, "UTM 23S's central meridian");
        point.Coordinates[1].Should().BeInRange(-20, -19.5);
    }

    [Fact]
    public void ReadPage_AnOffsetPastTheEnd_IsAnEmptyLastPage()
    {
        var path = TestVectorFiles.Write(_directory.Path, VectorFileFormat.FlatGeobuf, 3);

        var page = _reader.ReadPage(new VectorFileReadRequest(path, VectorFileFormat.FlatGeobuf, 50, 10), CancellationToken.None);

        page.Features.Should().BeEmpty();
        page.NextOffset.Should().BeNull();
    }

    [Fact]
    public void ReadPage_AMissingFile_ThrowsFileNotFound()
    {
        var act = () => _reader.ReadPage(new VectorFileReadRequest(_directory.File("missing.fgb"), VectorFileFormat.FlatGeobuf), CancellationToken.None);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ReadPage_AFileOfAnotherFormat_ThrowsInvalidData()
    {
        var path = _directory.File("garbage.fgb");
        File.WriteAllText(path, "not a flatgeobuf");

        var act = () => _reader.ReadPage(new VectorFileReadRequest(path, VectorFileFormat.FlatGeobuf), CancellationToken.None);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ReadPage_AnUnknownLayer_ThrowsInvalidData()
    {
        var path = TestVectorFiles.Write(_directory.Path, VectorFileFormat.FlatGeobuf, 1);

        var act = () => _reader.ReadPage(new VectorFileReadRequest(path, VectorFileFormat.FlatGeobuf) { LayerName = "nope" }, CancellationToken.None);

        act.Should().Throw<InvalidDataException>().WithMessage("*no layer 'nope'*");
    }

    [Fact]
    public void ReadPage_Cancelled_Throws()
    {
        var path = TestVectorFiles.Write(_directory.Path, VectorFileFormat.FlatGeobuf, 1);

        var act = () => _reader.ReadPage(new VectorFileReadRequest(path, VectorFileFormat.FlatGeobuf), new CancellationToken(canceled: true));

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void AMissingDriver_IsATypedErrorAtUse()
    {
        var path = TestVectorFiles.Write(_directory.Path, VectorFileFormat.GeoParquet, 1);
        using var driver = Ogr.GetDriverByName("Parquet");
        driver.Deregister();
        try
        {
            _reader.IsAvailable(VectorFileFormat.GeoParquet).Should().BeFalse();
            var act = () => _reader.ReadPage(new VectorFileReadRequest(path, VectorFileFormat.GeoParquet), CancellationToken.None);

            act.Should().Throw<VectorFileFormatUnavailableException>()
                .Where(ex => ex.Format == VectorFileFormat.GeoParquet)
                .WithMessage("*'Parquet' driver*");
        }
        finally
        {
            driver.Register();
        }
    }
}
