using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.Gdal.PMTiles;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests.PMTiles;

public sealed class PMTilesTileIdTests
{
    [Theory]
    [InlineData(0, 0, 0, 0UL)]
    [InlineData(1, 0, 0, 1UL)]
    [InlineData(1, 0, 1, 2UL)]
    [InlineData(1, 1, 1, 3UL)]
    [InlineData(1, 1, 0, 4UL)]
    [InlineData(2, 0, 0, 5UL)]
    [InlineData(12, 3423, 1763, 19078479UL)]
    public void FromTile_MatchesTheSpecsHilbertNumbering(int z, int x, int y, ulong expected)
    {
        PMTilesTileId.FromTile(new TileAddress(z, x, y)).Should().Be(expected);
        PMTilesTileId.ToTile(expected).Should().Be(new TileAddress(z, x, y));
    }

    [Fact]
    public void Ids_AreDenseAndRoundTrip_ForEveryTileUpToZoom6()
    {
        var expected = 0UL;
        for (var z = 0; z <= 6; z++)
        {
            var ids = new List<ulong>();
            for (var x = 0; x < 1 << z; x++)
            {
                for (var y = 0; y < 1 << z; y++)
                {
                    var tile = new TileAddress(z, x, y);
                    var id = PMTilesTileId.FromTile(tile);
                    PMTilesTileId.ToTile(id).Should().Be(tile);
                    ids.Add(id);
                }
            }

            ids.Order().Should().Equal(Enumerable.Range(0, 1 << (2 * z)).Select(i => expected + (ulong)i));
            expected += 1UL << (2 * z);
        }
    }

    [Fact]
    public void Ids_RoundTrip_AtTheDeepestZoom()
    {
        var corner = new TileAddress(TileAddress.MaxZoom, (1 << TileAddress.MaxZoom) - 1, (1 << TileAddress.MaxZoom) - 1);

        PMTilesTileId.ToTile(PMTilesTileId.FromTile(corner)).Should().Be(corner);
    }

    [Fact]
    public void ToTile_BeyondTheDeepestZoom_Throws()
    {
        var firstBeyond = PMTilesTileId.FromTile(new TileAddress(TileAddress.MaxZoom, 0, 0)) + (1UL << (2 * TileAddress.MaxZoom));

        var act = () => PMTilesTileId.ToTile(firstBeyond);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
