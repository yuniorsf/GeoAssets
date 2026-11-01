using FluentAssertions;
using GeoAssets.Domain.Sanitation.Icerp;
using Xunit;

namespace GeoAssets.Domain.Sanitation.Tests.Icerp;

public class TrenchDimensionsTests
{
    [Fact]
    public void VolumeCubicMeters_ComputesLengthWidthDepth()
    {
        var trench = new TrenchDimensions(LengthMeters: 4.0, WidthMeters: 0.6, DepthMeters: 1.2);

        trench.VolumeCubicMeters.Should().Be(4.0 * 0.6 * 1.2);
    }

    [Fact]
    public void DepthMeters_DefaultsToZero_SoVolumeIsZeroWithoutIt()
    {
        var trench = new TrenchDimensions(LengthMeters: 4.0, WidthMeters: 0.6);

        trench.DepthMeters.Should().Be(0);
        trench.VolumeCubicMeters.Should().Be(0);
    }

    [Fact]
    public void AreaSquareMeters_UnaffectedByDepth()
    {
        var trench = new TrenchDimensions(LengthMeters: 4.0, WidthMeters: 0.6, DepthMeters: 99);

        trench.AreaSquareMeters.Should().Be(4.0 * 0.6);
    }
}
