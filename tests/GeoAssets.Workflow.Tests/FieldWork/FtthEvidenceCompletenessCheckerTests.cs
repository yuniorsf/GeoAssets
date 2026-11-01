using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>XD01-278's AC: "All four mandatory photo slots must be filled before the checklist step can complete."</summary>
public class FtthEvidenceCompletenessCheckerTests
{
    private static Dictionary<string, string> AttributesWithSlots(params FtthEvidenceSlot[] slots) => new(StringComparer.Ordinal)
    {
        [ServiceOrderFtthEvidenceAttributes.CapturedSlotsKey] = System.Text.Json.JsonSerializer.Serialize(slots.Select(s => s.ToString())),
    };

    [Fact]
    public void IsComplete_NoAttributes_ReturnsFalse() =>
        FtthEvidenceCompletenessChecker.IsComplete(new Dictionary<string, string>(StringComparer.Ordinal)).Should().BeFalse();

    [Fact]
    public void IsComplete_ThreeOfFourSlotsCaptured_ReturnsFalse()
    {
        var attributes = AttributesWithSlots(FtthEvidenceSlot.CtoConnection, FtthEvidenceSlot.DropAnchorTag, FtthEvidenceSlot.PtoAndOnt);

        FtthEvidenceCompletenessChecker.IsComplete(attributes).Should().BeFalse();
    }

    [Fact]
    public void IsComplete_AllFourSlotsCaptured_ReturnsTrue()
    {
        var attributes = AttributesWithSlots(
            FtthEvidenceSlot.CtoConnection, FtthEvidenceSlot.DropAnchorTag, FtthEvidenceSlot.PtoAndOnt, FtthEvidenceSlot.CustomerSignOnGlass);

        FtthEvidenceCompletenessChecker.IsComplete(attributes).Should().BeTrue();
    }

    [Fact]
    public void MissingSlots_OnlyCustomerSignMissing_ReturnsExactlyThatSlot()
    {
        var attributes = AttributesWithSlots(FtthEvidenceSlot.CtoConnection, FtthEvidenceSlot.DropAnchorTag, FtthEvidenceSlot.PtoAndOnt);

        FtthEvidenceCompletenessChecker.MissingSlots(attributes).Should().BeEquivalentTo([FtthEvidenceSlot.CustomerSignOnGlass]);
    }

    [Fact]
    public void MissingSlots_NothingCaptured_ReturnsAllFour() =>
        FtthEvidenceCompletenessChecker.MissingSlots(new Dictionary<string, string>(StringComparer.Ordinal))
            .Should().BeEquivalentTo(FtthEvidenceCompletenessChecker.RequiredSlots);

    [Fact]
    public void RequiredSlots_ContainsExactlyTheFourDocumentedSlots() =>
        FtthEvidenceCompletenessChecker.RequiredSlots.Should().BeEquivalentTo(
        [
            FtthEvidenceSlot.CtoConnection,
            FtthEvidenceSlot.DropAnchorTag,
            FtthEvidenceSlot.PtoAndOnt,
            FtthEvidenceSlot.CustomerSignOnGlass,
        ]);
}
