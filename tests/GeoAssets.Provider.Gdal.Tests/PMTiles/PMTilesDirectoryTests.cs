using FluentAssertions;
using GeoAssets.Provider.Gdal.PMTiles;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests.PMTiles;

public sealed class PMTilesDirectoryTests
{
    private static PMTilesDirectory Directory(params (ulong TileId, ulong Offset, uint Length, uint RunLength)[] entries) =>
        PMTilesDirectory.Parse(PMTilesTestArchive.EncodeDirectory(entries));

    [Fact]
    public void Parse_DecodesDeltaIdsAndContiguousOffsets()
    {
        var directory = Directory((5, 0, 10, 1), (9, 10, 20, 3), (40, 100, 7, 0));

        directory.Entries.Should().Equal(
            new PMTilesDirectoryEntry(5, 0, 10, 1),
            new PMTilesDirectoryEntry(9, 10, 20, 3),
            new PMTilesDirectoryEntry(40, 100, 7, 0));
        directory.Entries[2].IsLeafDirectory.Should().BeTrue();
    }

    [Theory]
    [InlineData(5UL, 5UL)]
    [InlineData(9UL, 9UL)]
    [InlineData(11UL, 9UL)]
    [InlineData(40UL, 40UL)]
    [InlineData(1_000UL, 40UL)]
    public void Find_ReturnsTheCoveringEntry(ulong tileId, ulong expectedEntryId)
    {
        var directory = Directory((5, 0, 10, 1), (9, 10, 20, 3), (40, 100, 7, 0));

        directory.Find(tileId)!.Value.TileId.Should().Be(expectedEntryId);
    }

    [Theory]
    [InlineData(4UL)]
    [InlineData(6UL)]
    [InlineData(12UL)]
    public void Find_OutsideEveryRun_ReturnsNull(ulong tileId)
    {
        var directory = Directory((5, 0, 10, 1), (9, 10, 20, 3), (40, 100, 7, 0));

        directory.Find(tileId).Should().BeNull();
    }

    [Fact]
    public void Find_InAnEmptyDirectory_ReturnsNull() => Directory().Find(0).Should().BeNull();

    [Fact]
    public void Parse_TruncatedBytes_ThrowsFormatException()
    {
        var bytes = PMTilesTestArchive.EncodeDirectory([(5, 0, 10, 1), (9, 10, 20, 3)]);

        var act = () => PMTilesDirectory.Parse(bytes.AsSpan(0, bytes.Length - 1));

        act.Should().Throw<PMTilesFormatException>();
    }

    [Fact]
    public void Parse_AnEntryCountLargerThanTheBytes_ThrowsFormatException()
    {
        var act = () => PMTilesDirectory.Parse([0xFF, 0x7F]);

        act.Should().Throw<PMTilesFormatException>().WithMessage("*can't hold*");
    }

    [Fact]
    public void Parse_AFirstEntryContinuingAPreviousOne_ThrowsFormatException()
    {
        byte[] bytes = [1, 0, 1, 1, 0];

        var act = () => PMTilesDirectory.Parse(bytes);

        act.Should().Throw<PMTilesFormatException>();
    }
}
