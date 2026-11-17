using FluentAssertions;
using GeoAssets.Shared.Services.OrderTargets;
using Xunit;
using static GeoAssets.Shared.Tests.Services.OrderTargets.OrderTargetPreviewFactory;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>The preview panel's derived numbers and the apply gate (XD01-312).</summary>
public sealed class SelectionPreviewSummaryTests
{
    [Fact]
    public void From_Null_Throws() =>
        FluentActions.Invoking(() => SelectionPreviewSummary.From(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void ZeroMatches_HasNothingToAdd_AndCannotApply()
    {
        var summary = SelectionPreviewSummary.From(Preview(matched: 0));

        summary.ToAdd.Should().Be(0);
        summary.HasNothingToAdd.Should().BeTrue();
        summary.CanApply(truncationAccepted: true).Should().BeFalse();
    }

    [Fact]
    public void EverythingAlreadyInTheOrder_HasNothingToAdd()
    {
        var summary = SelectionPreviewSummary.From(Preview(matched: 4, alreadyInOrder: 4));

        summary.HasNothingToAdd.Should().BeTrue();
        summary.CanApply(false).Should().BeFalse();
    }

    [Fact]
    public void NotTruncated_CanApplyWithoutAnyAcceptance()
    {
        var summary = SelectionPreviewSummary.From(Preview(matched: 5, alreadyInOrder: 1));

        summary.ToAdd.Should().Be(4);
        summary.IsTruncated.Should().BeFalse();
        summary.CanApply(false).Should().BeTrue();
    }

    [Fact]
    public void Truncated_RequiresTheExplicitAcceptance()
    {
        var summary = SelectionPreviewSummary.From(Preview(matched: 10, truncated: 4));

        summary.ToAdd.Should().Be(6);
        summary.IsTruncated.Should().BeTrue();
        summary.CanApply(false).Should().BeFalse();
        summary.CanApply(true).Should().BeTrue();
    }

    [Fact]
    public void TruncatedToNothing_CannotApplyEvenWhenAccepted()
    {
        var summary = SelectionPreviewSummary.From(Preview(matched: 3, truncated: 3));

        summary.ToAdd.Should().Be(0);
        summary.CanApply(true).Should().BeFalse();
    }

    [Fact]
    public void WarningsAndBlockedTargets_DoNotPreventApplying() =>
        SelectionPreviewSummary.From(Preview(matched: 3, warnings: 1, blocked: 2)).CanApply(false).Should().BeTrue();

    [Fact]
    public void NotResolvedCount_IsTheIdentifiersTheServerDidNotTake() =>
        SelectionPreviewSummary.From(Preview(notFound: ["x", "y"])).NotResolvedCount.Should().Be(2);
}
