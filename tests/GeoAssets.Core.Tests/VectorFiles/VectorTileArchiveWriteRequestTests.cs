using FluentAssertions;
using GeoAssets.Core.VectorFiles;
using Xunit;

namespace GeoAssets.Core.Tests.VectorFiles;

public sealed class VectorTileArchiveWriteRequestTests
{
    [Fact]
    public void ARequest_DefaultsToZooms0To14_EveryAttribute_AndTheDriversTileGeometry()
    {
        var request = new VectorTileArchiveWriteRequest();

        request.MinZoom.Should().Be(0);
        request.MaxZoom.Should().Be(14);
        request.LayerNames.Should().BeNull();
        request.Attributes.Should().BeNull();
        request.Extent.Should().Be(4096);
        request.Buffer.Should().Be(80);
        request.Simplification.Should().BeNull();
        request.SimplificationMaxZoom.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 9)]
    [InlineData(0, VectorTileArchiveWriteRequest.MaxSupportedZoom)]
    public void AValidZoomRange_IsKept(int minZoom, int maxZoom)
    {
        var request = new VectorTileArchiveWriteRequest(minZoom, maxZoom);

        request.MinZoom.Should().Be(minZoom);
        request.MaxZoom.Should().Be(maxZoom);
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(0, VectorTileArchiveWriteRequest.MaxSupportedZoom + 1)]
    [InlineData(8, 7)]
    public void AnInvalidZoomRange_IsRejected(int minZoom, int maxZoom)
    {
        var act = () => new VectorTileArchiveWriteRequest(minZoom, maxZoom);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AFeature_GoesToItsAssetTypesLayer_ElseToALayerNamedAfterTheAssetType()
    {
        var request = new VectorTileArchiveWriteRequest { LayerNames = new Dictionary<string, string>(StringComparer.Ordinal) { ["type-a"] = "Pole" } };

        request.LayerNameOf("type-a").Should().Be("Pole");
        request.LayerNameOf("type-b").Should().Be("type-b");
        request.LayerNameOf(" ").Should().Be(VectorTileArchiveWriteRequest.UnclassifiedLayerName);
        request.LayerNameOf(null).Should().Be(VectorTileArchiveWriteRequest.UnclassifiedLayerName);
        new VectorTileArchiveWriteRequest().LayerNameOf("type-a").Should().Be("type-a");
    }

    [Fact]
    public void ABlankLayerName_IsRejected()
    {
        var act = () => new VectorTileArchiveWriteRequest { LayerNames = new Dictionary<string, string>(StringComparer.Ordinal) { ["type-a"] = " " } };

        act.Should().Throw<ArgumentException>().WithMessage("*'type-a'*blank*");
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(65_536)]
    public void APowerOfTwoExtent_IsKept(int extent)
    {
        new VectorTileArchiveWriteRequest { Extent = extent }.Extent.Should().Be(extent);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(1000)]
    [InlineData(131_072)]
    public void AnyOtherExtent_IsRejected(int extent)
    {
        var act = () => new VectorTileArchiveWriteRequest { Extent = extent };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(VectorTileArchiveWriteRequest.MaxBuffer + 1)]
    public void AnUnboundedBuffer_IsRejected(int buffer)
    {
        var act = () => new VectorTileArchiveWriteRequest { Buffer = buffer };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(VectorTileArchiveWriteRequest.MaxBuffer)]
    public void ABoundedBuffer_IsKept(int buffer)
    {
        new VectorTileArchiveWriteRequest { Buffer = buffer }.Buffer.Should().Be(buffer);
    }

    [Fact]
    public void SimplificationTolerances_AreKept()
    {
        var request = new VectorTileArchiveWriteRequest { Simplification = 2.5, SimplificationMaxZoom = 0 };

        request.Simplification.Should().Be(2.5);
        request.SimplificationMaxZoom.Should().Be(0);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnInvalidSimplificationTolerance_IsRejected(double tolerance)
    {
        var simplification = () => new VectorTileArchiveWriteRequest { Simplification = tolerance };
        var atMaxZoom = () => new VectorTileArchiveWriteRequest { SimplificationMaxZoom = tolerance };

        simplification.Should().Throw<ArgumentOutOfRangeException>();
        atMaxZoom.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DeclaredAttributes_AreKept()
    {
        new VectorTileArchiveWriteRequest { Attributes = ["kind", "owner"] }.Attributes.Should().Equal("kind", "owner");
    }

    [Fact]
    public void TheUnavailableException_IsANotSupportedException()
    {
        var inner = new InvalidOperationException("inner");

        new VectorTileArchiveUnavailableException().Should().BeAssignableTo<NotSupportedException>();
        new VectorTileArchiveUnavailableException("no driver").Message.Should().Be("no driver");
        new VectorTileArchiveUnavailableException("no driver", inner).InnerException.Should().BeSameAs(inner);
    }
}
