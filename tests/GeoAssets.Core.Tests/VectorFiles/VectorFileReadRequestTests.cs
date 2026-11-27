using FluentAssertions;
using GeoAssets.Core.VectorFiles;
using Xunit;

namespace GeoAssets.Core.Tests.VectorFiles;

public sealed class VectorFileReadRequestTests
{
    [Fact]
    public void ARequest_DefaultsToTheFirstPageOfTheFirstLayer()
    {
        var request = new VectorFileReadRequest("poles.fgb", VectorFileFormat.FlatGeobuf);

        request.Offset.Should().Be(0);
        request.Limit.Should().Be(VectorFileReadRequest.DefaultLimit);
        request.LayerName.Should().BeNull();
    }

    [Theory]
    [InlineData(-1L, 10)]
    [InlineData(0L, 0)]
    [InlineData(0L, VectorFileReadRequest.MaxLimit + 1)]
    public void AnUnboundedOrNegativePage_IsRejected(long offset, int limit)
    {
        var act = () => new VectorFileReadRequest("poles.fgb", VectorFileFormat.FlatGeobuf, offset, limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(VectorFileFormat.None)]
    [InlineData((VectorFileFormat)42)]
    public void AnUnknownFormat_IsRejected(VectorFileFormat format)
    {
        var act = () => new VectorFileReadRequest("poles.fgb", format);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ABlankPath_IsRejected(string path)
    {
        var act = () => new VectorFileReadRequest(path, VectorFileFormat.GeoParquet);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheUnavailableError_NamesTheFormat()
    {
        var error = new VectorFileFormatUnavailableException(VectorFileFormat.GeoParquet, "no Parquet driver.");

        error.Format.Should().Be(VectorFileFormat.GeoParquet);
        error.Message.Should().Be("GeoParquet files can't be read or written here: no Parquet driver.");
        error.Should().BeAssignableTo<NotSupportedException>();
    }
}
