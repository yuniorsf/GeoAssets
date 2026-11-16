using FluentAssertions;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

public class OrderTargetPolicyTests
{
    private const string OwningTypeId = "pole-inspection";

    private static OrderTargetPolicy ValidPolicy() => new()
    {
        TargetAssetTypeIds = ["pole", "manhole"],
        AliasAttributeKey = "tag",
        SelectionLimit = 500,
        Activities = [new("inspect", "Inspect"), new("replace", "Replace")],
        Exclusivity = OrderTargetExclusivity.BlockOnOverlap,
        FanOut = new OrderTargetFanOutPolicy("pole-inspection-notice"),
    };

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_FullyPopulatedPolicy_DoesNotThrow()
    {
        var act = () => ValidPolicy().Validate(OwningTypeId);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_OnlyRequiredMembersSet_DoesNotThrow()
    {
        var policy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().NotThrow();
        policy.AliasAttributeKey.Should().BeNull();
        policy.SelectionLimit.Should().BeNull();
        policy.Activities.Should().BeEmpty();
        policy.FanOut.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankOwningOrderTypeId_ThrowsArgumentException(string? blankId)
    {
        var act = () => ValidPolicy().Validate(blankId!);

        act.Should().Throw<ArgumentException>().WithParameterName("owningOrderTypeId");
    }

    // ── POL-G1 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_POL_G1_EmptyTargetAssetTypeIds_ThrowsArgumentException()
    {
        var policy = ValidPolicy() with { TargetAssetTypeIds = [] };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().ThrowExactly<ArgumentException>().WithMessage($"*'{OwningTypeId}'*at least one target asset type*");
    }

    [Fact]
    public void Validate_POL_G1_NullTargetAssetTypeIds_ThrowsArgumentException()
    {
        // Reachable through JSON: "targetAssetTypeIds": null satisfies the 'required' keyword.
        var policy = ValidPolicy() with { TargetAssetTypeIds = null! };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().ThrowExactly<ArgumentException>();
    }

    // ── POL-G2 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_POL_G2_SelectionLimitZeroOrNegative_ThrowsArgumentOutOfRangeException(int selectionLimit)
    {
        var policy = ValidPolicy() with { SelectionLimit = selectionLimit };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>()
            .Which.ActualValue.Should().Be(selectionLimit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Validate_POL_G2_SelectionLimitNullOrPositive_DoesNotThrow(int? selectionLimit)
    {
        var policy = ValidPolicy() with { SelectionLimit = selectionLimit };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().NotThrow();
    }

    // ── POL-G3 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("inspect")]
    [InlineData("INSPECT")]
    public void Validate_POL_G3_DuplicateActivityIds_ThrowsArgumentException(string secondId)
    {
        var policy = ValidPolicy() with
        {
            Activities = [new("inspect", "Inspect"), new(secondId, "Inspect again")],
        };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().ThrowExactly<ArgumentException>().WithMessage($"*'{secondId}'*more than once*");
    }

    [Fact]
    public void Validate_POL_G3_SameDisplayNameDifferentIds_DoesNotThrow()
    {
        var policy = ValidPolicy() with
        {
            Activities = [new("inspect", "Work"), new("replace", "Work")],
        };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().NotThrow();
    }

    // ── POL-G4 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Validate_POL_G4_BlankAliasAttributeKey_ThrowsArgumentException(string aliasAttributeKey)
    {
        var policy = ValidPolicy() with { AliasAttributeKey = aliasAttributeKey };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().ThrowExactly<ArgumentException>().WithMessage("*alias attribute key*");
    }

    [Fact]
    public void Validate_POL_G4_NullAliasAttributeKey_DoesNotThrow()
    {
        var policy = ValidPolicy() with { AliasAttributeKey = null };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().NotThrow();
    }

    // ── POL-G5 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_POL_G5_BlankFanOutChildOrderTypeId_ThrowsArgumentException(string childOrderTypeId)
    {
        var policy = ValidPolicy() with { FanOut = new OrderTargetFanOutPolicy(childOrderTypeId) };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().ThrowExactly<ArgumentException>().WithMessage("*child order type id must not be blank*");
    }

    [Theory]
    [InlineData("pole-inspection")]
    [InlineData("POLE-INSPECTION")]
    public void Validate_POL_G5_FanOutChildEqualsOwningOrderTypeId_ThrowsArgumentException(string childOrderTypeId)
    {
        var policy = ValidPolicy() with { FanOut = new OrderTargetFanOutPolicy(childOrderTypeId) };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().ThrowExactly<ArgumentException>().WithMessage("*cannot fan out into itself*");
    }

    [Fact]
    public void Validate_POL_G5_NullFanOut_DoesNotThrow()
    {
        var policy = ValidPolicy() with { FanOut = null };

        var act = () => policy.Validate(OwningTypeId);

        act.Should().NotThrow();
    }

    // ── POL-B1 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Exclusivity_POL_B1_DefaultsToNone()
    {
        var policy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] };

        policy.Exclusivity.Should().Be(OrderTargetExclusivity.None);
        default(OrderTargetExclusivity).Should().Be(OrderTargetExclusivity.None);
    }

    [Fact]
    public void FanOutPolicy_PartyKind_DefaultsToOccupantOrganization()
    {
        new OrderTargetFanOutPolicy("child").PartyKind.Should().Be(OrderTargetFanOutPartyKind.OccupantOrganization);
    }
}
