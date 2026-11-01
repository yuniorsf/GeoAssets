using FluentAssertions;
using GeoAssets.Shared.Services.Evidence;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Evidence;

public class GpsAccuracyGateTests
{
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(9.9, true)]
    [InlineData(10.0, true)] // boundary, inclusive
    [InlineData(10.1, false)]
    [InlineData(50.0, false)]
    public void IsAcceptable_EvaluatesHorizontalAccuracyThreshold(double accuracyMeters, bool expected)
    {
        var fix = new GpsFix(0, 0, accuracyMeters, IsFromMockProvider: false);

        GpsAccuracyGate.IsAcceptable(fix).Should().Be(expected);
    }

    [Fact]
    public void IsAcceptable_MockProvider_IsRejectedEvenWithPerfectAccuracy()
    {
        var fix = new GpsFix(0, 0, HorizontalAccuracyMeters: 0.1, IsFromMockProvider: true);

        GpsAccuracyGate.IsAcceptable(fix).Should().BeFalse();
    }
}
