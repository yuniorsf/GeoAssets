using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors;

/// <summary>XD01-241's AC: "Unit tests cover TotalUS = Σ(ItemQuantity × FactorUS × RegionalMultiplier)".</summary>
public class UsTotalCalculatorTests
{
    private static UsCatalogItem Item(decimal baseUsFactor, decimal regionalMultiplier) => new()
    {
        ItemCode = "US-EL-014",
        BaseUsFactor = baseUsFactor,
        RegionalMultiplier = regionalMultiplier,
    };

    [Fact]
    public void TotalUs_SingleLine_MultipliesQuantityFactorAndRegionalMultiplier()
    {
        var lines = new[] { (Item(baseUsFactor: 2.5m, regionalMultiplier: 1.2m), Quantity: 10m) };

        UsTotalCalculator.TotalUs(lines).Should().Be(10m * 2.5m * 1.2m);
    }

    [Fact]
    public void TotalUs_MultipleLines_SumsEachLineIndependently()
    {
        var lines = new[]
        {
            (Item(baseUsFactor: 2.5m, regionalMultiplier: 1.2m), Quantity: 10m),
            (Item(baseUsFactor: 1.0m, regionalMultiplier: 1.0m), Quantity: 5m),
        };

        var expected = (10m * 2.5m * 1.2m) + (5m * 1.0m * 1.0m);
        UsTotalCalculator.TotalUs(lines).Should().Be(expected);
    }

    [Fact]
    public void TotalUs_EmptyLineList_IsZero() =>
        UsTotalCalculator.TotalUs([]).Should().Be(0);

    [Fact]
    public void TotalUs_RegionalMultiplierOfOne_EqualsBaseFactorTimesQuantity()
    {
        var lines = new[] { (Item(baseUsFactor: 3m, regionalMultiplier: 1m), Quantity: 4m) };

        UsTotalCalculator.TotalUs(lines).Should().Be(12m);
    }

    [Fact]
    public void TotalUs_NullLines_Throws()
    {
        var act = () => UsTotalCalculator.TotalUs(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
