using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using Xunit;
using static GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy.OccupancyTestData;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy;

/// <summary><see cref="OccupancyAssetAttributes"/> (XD01-318): what a committed pole carries for the thematic layer.</summary>
public sealed class OccupancyAssetAttributesTests
{
    [Theory]
    [InlineData(OccupancyClass.None, "None")]
    [InlineData(OccupancyClass.Low, "Low")]
    [InlineData(OccupancyClass.High, "High")]
    [InlineData(OccupancyClass.OverCapacity, "OverCapacity")]
    [InlineData(OccupancyClass.Unidentified, "Unidentified")]
    public void ClassValue_IsTheClassMemberName(OccupancyClass occupancyClass, string expected)
    {
        OccupancyAssetAttributes.ClassValue(occupancyClass).Should().Be(expected);
    }

    [Fact]
    public void ClassValue_UndefinedClass_ThrowsArgumentOutOfRangeException()
    {
        var act = () => OccupancyAssetAttributes.ClassValue((OccupancyClass)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void For_NullOccupancy_ThrowsArgumentNullException()
    {
        var act = () => OccupancyAssetAttributes.For(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void For_AnEmptyPole_IsClassNone_WithNoFlags()
    {
        OccupancyAssetAttributes.For(PoleOf()).Should().Equal(Expected("None", "0", "false", "false"));
    }

    [Fact]
    public void For_AFullPole_IsClassHigh()
    {
        OccupancyAssetAttributes.For(PoleOf([.. Entries(6)])).Should().Equal(Expected("High", "6", "false", "false"));
    }

    [Fact]
    public void For_APoleBeyondItsSlots_IsClassOverCapacity_AndFlaggedSo()
    {
        OccupancyAssetAttributes.For(PoleOf([.. Entries(6, beyond: 2)])).Should().Equal(Expected("OverCapacity", "8", "true", "false"));
    }

    [Fact]
    public void For_APoleBothOverCapacityAndWithAnUnidentifiedOccupant_IsClassUnidentified_AndKeepsBothFlags()
    {
        var pole = PoleOf([.. Entries(6), Entry(null, identified: false)]);

        OccupancyAssetAttributes.For(pole).Should().Equal(Expected("Unidentified", "7", "true", "true"));
    }

    [Fact]
    public void For_APartlyOccupiedPole_IsClassLow()
    {
        OccupancyAssetAttributes.For(PoleOf([.. Entries(2)])).Should().Equal(Expected("Low", "2", "false", "false"));
    }

    private static Dictionary<string, string> Expected(string occupancyClass, string occupied, string overCapacity, string unidentified) => new(StringComparer.Ordinal)
    {
        [OccupancyAssetAttributes.ClassKey] = occupancyClass,
        [OccupancyAssetAttributes.OccupiedCountKey] = occupied,
        [OccupancyAssetAttributes.OverCapacityKey] = overCapacity,
        [OccupancyAssetAttributes.UnidentifiedKey] = unidentified,
    };
}
