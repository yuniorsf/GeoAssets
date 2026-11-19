using FluentAssertions;
using GeoAssets.IpBilling.UI.Components;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.IpBilling;

/// <summary>The chart editor's row validation (XD01-301), exercised as the pure function it is.</summary>
public class DistribuidoraLampWattageEditorTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(70, 14.5)]
    [InlineData(400, 0)]
    public void ValidateRow_ValidRow_ReturnsNull(int wattageW, double ballastLossW) =>
        DistribuidoraLampWattageEditor.ValidateRow(wattageW, (decimal)ballastLossW).Should().BeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-70)]
    public void ValidateRow_NonPositiveWattage_ReturnsTheWattageKey(int wattageW) =>
        DistribuidoraLampWattageEditor.ValidateRow(wattageW, 0m).Should().Be("admin.ipBilling.distribuidoras.wattageInvalid");

    [Fact]
    public void ValidateRow_NegativeBallastLoss_ReturnsTheBallastLossKey() =>
        DistribuidoraLampWattageEditor.ValidateRow(70, -0.01m).Should().Be("admin.ipBilling.distribuidoras.ballastLossInvalid");

    [Fact]
    public void ValidateRow_BothInvalid_ReportsTheWattageFirst() =>
        DistribuidoraLampWattageEditor.ValidateRow(0, -1m).Should().Be("admin.ipBilling.distribuidoras.wattageInvalid");

    [Fact]
    public void PageSizes_StayWithinTheServersCap()
    {
        DistribuidoraLampWattageEditor.DefaultPageSize.Should().BeInRange(1, 100);
        DistribuidoraList.PageSize.Should().BeInRange(1, 100);
        MunicipioForm.DistribuidoraPickerPageSize.Should().BeInRange(1, 100);
    }
}
