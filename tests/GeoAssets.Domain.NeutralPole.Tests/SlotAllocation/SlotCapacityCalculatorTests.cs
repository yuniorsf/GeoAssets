using FluentAssertions;
using GeoAssets.Domain.NeutralPole.SlotAllocation;
using Xunit;

namespace GeoAssets.Domain.NeutralPole.Tests.SlotAllocation;

public class SlotCapacityCalculatorTests
{
    [Fact]
    public void CalculateMaxSlots_ExactMultipleOfStandardBand_DividesEvenly()
    {
        SlotCapacityCalculator.CalculateMaxSlots(3000).Should().Be(6);
    }

    [Fact]
    public void CalculateMaxSlots_PartialBandRemainder_RoundsDown()
    {
        SlotCapacityCalculator.CalculateMaxSlots(2600).Should().Be(5, "2600mm holds 5 full 500mm bands plus a 100mm remainder too small for a 6th");
    }

    [Fact]
    public void CalculateMaxSlots_CustomBandHeight_IsRespected()
    {
        SlotCapacityCalculator.CalculateMaxSlots(1000, bandHeightMillimeters: 250).Should().Be(4);
    }

    [Fact]
    public void CalculateMaxSlots_NegativeHeight_Throws()
    {
        var act = () => SlotCapacityCalculator.CalculateMaxSlots(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CalculateMaxSlots_ZeroBandHeight_Throws()
    {
        var act = () => SlotCapacityCalculator.CalculateMaxSlots(1000, bandHeightMillimeters: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
