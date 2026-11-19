using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using Xunit;
using static GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy.OccupancyTestData;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy;

public sealed class OccupancyClassifierTests
{
    [Theory]
    [InlineData(0, OccupancyClass.None)]
    [InlineData(1, OccupancyClass.Low)]
    [InlineData(5, OccupancyClass.Low)]
    [InlineData(6, OccupancyClass.High)]
    [InlineData(7, OccupancyClass.OverCapacity)]
    [InlineData(40, OccupancyClass.OverCapacity)]
    [InlineData(int.MaxValue, OccupancyClass.OverCapacity)]
    public void Classify_EveryOccupantIdentified_GoesByTheCount(int occupiedCount, OccupancyClass expected)
    {
        OccupancyClassifier.Classify(occupiedCount, hasUnidentifiedOccupant: false).Should().Be(expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(40)]
    public void Classify_AnyUnidentifiedOccupant_IsUnidentifiedWhateverTheCount(int occupiedCount)
    {
        OccupancyClassifier.Classify(occupiedCount, hasUnidentifiedOccupant: true).Should().Be(OccupancyClass.Unidentified);
    }

    [Fact]
    public void Classify_NegativeCount_ThrowsArgumentOutOfRange()
    {
        var act = () => OccupancyClassifier.Classify(-1, hasUnidentifiedOccupant: false);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Classify_NullOccupancy_ThrowsArgumentNull()
    {
        var act = () => OccupancyClassifier.Classify(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Classes_GrowWithSeverity_UnidentifiedOverOverCapacityOverHighOverLowOverNone()
    {
        Enum.GetValues<OccupancyClass>().Order().Should().Equal(
            OccupancyClass.None, OccupancyClass.Low, OccupancyClass.High, OccupancyClass.OverCapacity, OccupancyClass.Unidentified);
    }

    [Theory]
    [InlineData(0, 0, OccupancyClass.None)]
    [InlineData(3, 0, OccupancyClass.Low)]
    [InlineData(6, 0, OccupancyClass.High)]
    [InlineData(6, 1, OccupancyClass.OverCapacity)]
    [InlineData(5, 1, OccupancyClass.High)]
    public void Classify_Occupancy_UsesItsOccupiedCount(int regulated, int beyond, OccupancyClass expected)
    {
        var occupancy = PoleOf([.. Entries(regulated, beyond)]);

        OccupancyClassifier.Classify(occupancy).Should().Be(expected);
        occupancy.Class.Should().Be(expected);
    }

    [Fact]
    public void Classify_Occupancy_BothIrregularitiesAtOnce_KeepsBothFlags_AndIsUnidentified()
    {
        var occupancy = PoleOf([.. Entries(regulated: 6, beyond: 1), Entry(null, PoleAttachmentOccupantStatus.ClandestineUnauthorized, identified: false)]);

        occupancy.IsOverCapacity.Should().BeTrue();
        occupancy.HasUnidentifiedOccupant.Should().BeTrue();
        OccupancyClassifier.Classify(occupancy).Should().Be(OccupancyClass.Unidentified);
    }

    [Fact]
    public void Classify_Occupancy_ReservationsAreNotOccupants_NeitherCountedNorUnidentified()
    {
        var occupancy = PoleOf(
            Entry(1, PoleAttachmentOccupantStatus.ActiveReservation, identified: false),
            Entry(2, PoleAttachmentOccupantStatus.ActiveReservation));

        occupancy.OccupiedCount.Should().Be(0);
        occupancy.HasUnidentifiedOccupant.Should().BeFalse();
        OccupancyClassifier.Classify(occupancy).Should().Be(OccupancyClass.None);
    }
}
