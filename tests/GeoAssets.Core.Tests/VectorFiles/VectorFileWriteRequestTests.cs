using FluentAssertions;
using GeoAssets.Core.VectorFiles;
using Xunit;

namespace GeoAssets.Core.Tests.VectorFiles;

public sealed class VectorFileWriteRequestTests
{
    [Fact]
    public void ARequest_DefaultsToEveryAttribute_InBatchesOfTheDefaultSize()
    {
        var request = new VectorFileWriteRequest(VectorFileFormat.GeoParquet);

        request.Format.Should().Be(VectorFileFormat.GeoParquet);
        request.Attributes.Should().BeNull();
        request.BatchSize.Should().Be(VectorFileWriteRequest.DefaultBatchSize);
    }

    [Theory]
    [InlineData(VectorFileFormat.None)]
    [InlineData((VectorFileFormat)42)]
    public void AnUnknownFormat_IsRejected(VectorFileFormat format)
    {
        var act = () => new VectorFileWriteRequest(format);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(VectorFileReadRequest.MaxLimit + 1)]
    public void AnUnboundedBatch_IsRejected(int batchSize)
    {
        var act = () => new VectorFileWriteRequest(VectorFileFormat.FlatGeobuf) { BatchSize = batchSize };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(VectorFileReadRequest.MaxLimit)]
    public void ABoundedBatch_IsKept(int batchSize)
    {
        new VectorFileWriteRequest(VectorFileFormat.FlatGeobuf) { BatchSize = batchSize }.BatchSize.Should().Be(batchSize);
    }

    [Fact]
    public void DeclaredAttributes_AreKept()
    {
        new VectorFileWriteRequest(VectorFileFormat.FlatGeobuf) { Attributes = ["kind", "owner"] }.Attributes.Should().Equal("kind", "owner");
    }
}
