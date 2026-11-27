using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.Gdal.PMTiles;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests.PMTiles;

public sealed class PMTilesHeaderTests
{
    [Fact]
    public void Parse_ReadsEveryField()
    {
        var bytes = new PMTilesTestArchive { MinZoom = 2, MaxZoom = 9 }
            .Add(new TileAddress(2, 1, 1), [1, 2, 3])
            .Build();

        var header = PMTilesHeader.Parse(bytes);

        header.Version.Should().Be(3);
        header.RootDirectoryOffset.Should().Be(PMTilesHeader.Size);
        header.InternalCompression.Should().Be(PMTilesCompression.Gzip);
        header.TileCompression.Should().Be(PMTilesCompression.Gzip);
        header.TileType.Should().Be(PMTilesTileType.Mvt);
        header.Clustered.Should().BeTrue();
        header.MinZoom.Should().Be(2);
        header.MaxZoom.Should().Be(9);
        header.TileEntriesCount.Should().Be(1);
        header.TileDataLength.Should().Be(3);
        header.Bounds.Should().Be((-180d, -85d, 180d, 85d));
        header.CenterZoom.Should().Be(2);
        header.Center.Should().Be((1d, -2d));
    }

    [Fact]
    public void Parse_WrongMagic_Throws()
    {
        var bytes = new PMTilesTestArchive().Build();
        bytes[0] = (byte)'X';

        var act = () => PMTilesHeader.Parse(bytes);

        act.Should().Throw<PMTilesFormatException>().WithMessage("*magic*");
    }

    [Fact]
    public void Parse_AnotherVersion_Throws()
    {
        var bytes = new PMTilesTestArchive().Build();
        bytes[7] = 2;

        var act = () => PMTilesHeader.Parse(bytes);

        act.Should().Throw<PMTilesFormatException>().WithMessage("*version 2*");
    }

    [Fact]
    public void Parse_TooShort_Throws()
    {
        var act = () => PMTilesHeader.Parse(new byte[100]);

        act.Should().Throw<PMTilesFormatException>();
    }
}
