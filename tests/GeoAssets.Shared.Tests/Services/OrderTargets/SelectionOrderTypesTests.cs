using FluentAssertions;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>The policy-aware order type picker (XD01-312).</summary>
public sealed class SelectionOrderTypesTests
{
    private static readonly Dictionary<string, string> _loaded = new(StringComparer.Ordinal)
    {
        ["p1"] = "pole", ["p2"] = "pole", ["c1"] = "cable", ["m1"] = "manhole",
    };

    private static string? AssetTypeOf(string id) => _loaded.GetValueOrDefault(id);

    private static OrderType Type(string id, int? limit = null, params string[] targetTypes) => new()
    {
        Id = id,
        DisplayName = id,
        TargetPolicy = targetTypes.Length == 0 ? null : new OrderTargetPolicy { TargetAssetTypeIds = targetTypes, SelectionLimit = limit },
    };

    [Fact]
    public void Evaluate_NullArguments_Throw()
    {
        FluentActions.Invoking(() => SelectionOrderTypes.Evaluate(null!, [], AssetTypeOf)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => SelectionOrderTypes.Evaluate([], null!, AssetTypeOf)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => SelectionOrderTypes.Evaluate([], [], null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_TypeWithoutATargetPolicy_IsNeverOffered()
    {
        var fits = SelectionOrderTypes.Evaluate([Type("plain")], ["p1"], AssetTypeOf);

        fits.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_OffersOnlyTypesTargetingASelectedFeaturesAssetType_InTheGivenOrder_WithTheCounts()
    {
        var fits = SelectionOrderTypes.Evaluate(
            [Type("cables", null, "cable"), Type("lighting", null, "lamp"), Type("poles", null, "pole", "manhole")],
            ["p1", "p2", "c1", "m1"],
            AssetTypeOf);

        fits.Select(f => f.Type.Id).Should().Equal("cables", "poles");
        fits[0].Should().Match<SelectionOrderTypeFit>(f => f.EligibleCount == 1 && f.OtherTypeCount == 3 && f.UnknownCount == 0);
        fits[1].Should().Match<SelectionOrderTypeFit>(f => f.EligibleCount == 3 && f.OtherTypeCount == 1 && f.UnknownCount == 0);
    }

    [Fact]
    public void Evaluate_IdsTheClientHasNotLoaded_KeepATypeOnOffer_TheServerDecides()
    {
        var fits = SelectionOrderTypes.Evaluate([Type("lighting", null, "lamp")], ["unloaded", "c1"], AssetTypeOf);

        fits.Should().ContainSingle().Which.Should().Match<SelectionOrderTypeFit>(
            f => f.EligibleCount == 0 && f.UnknownCount == 1 && f.OtherTypeCount == 1);
    }

    [Fact]
    public void Evaluate_EmptySelection_OffersNothing() =>
        SelectionOrderTypes.Evaluate([Type("poles", null, "pole")], [], AssetTypeOf).Should().BeEmpty();

    [Fact]
    public void Evaluate_ComparesAssetTypesOrdinally_AsTheServerDoes() =>
        SelectionOrderTypes.Evaluate([Type("poles", null, "POLE")], ["p1"], AssetTypeOf).Should().BeEmpty();

    [Theory]
    [InlineData(null, 3, 0, false)]
    [InlineData(3, 2, 0, false)]
    [InlineData(3, 3, 0, false)]
    [InlineData(3, 4, 0, true)]
    [InlineData(3, 2, 2, true)]
    public void MayExceedLimit_BelowAtOrAboveTheTypesLimit(int? limit, int eligible, int unknown, bool expected)
    {
        var fit = new SelectionOrderTypeFit(Type("poles", limit, "pole"), eligible, 0, unknown);

        fit.SelectionLimit.Should().Be(limit);
        fit.MayExceedLimit.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 5, false)]
    public void UseSelectionInitially_OnlyWhenTheMapAskedAndSomethingIsSelected(bool fromSelection, int count, bool expected) =>
        SelectionOrderTypes.UseSelectionInitially(fromSelection, count).Should().Be(expected);

    [Fact]
    public void KeepOrFirst_KeepsAnOfferedChoice_FallsBackToTheFirst_OrEmpty()
    {
        IReadOnlyList<OrderType> offered = [Type("a"), Type("b")];

        SelectionOrderTypes.KeepOrFirst("b", offered).Should().Be("b");
        SelectionOrderTypes.KeepOrFirst("gone", offered).Should().Be("a");
        SelectionOrderTypes.KeepOrFirst(null, offered).Should().Be("a");
        SelectionOrderTypes.KeepOrFirst("b", []).Should().BeEmpty();
        FluentActions.Invoking(() => SelectionOrderTypes.KeepOrFirst("a", null!)).Should().Throw<ArgumentNullException>();
    }
}
