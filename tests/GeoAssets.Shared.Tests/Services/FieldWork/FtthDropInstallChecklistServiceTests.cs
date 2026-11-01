using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services.FieldWork;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.FieldWork;

/// <summary>
/// XD01-277's AC: a technician can complete a full drop install end to end; any OPM failure
/// blocks progression with an on-screen explanation; the completed payload carries every field
/// the checklist collected.
/// </summary>
public class FtthDropInstallChecklistServiceTests
{
    private static readonly OrderType FtthOrderType = FtthDropInstallWorkflowGraph.BuildOrderType("ftth-drop-install", "FTTH Drop Install");
    private static readonly OrderType CtoRepairOrderType = new() { Id = "cto-repair", DisplayName = "CTO Repair", InitialStateKey = "Open" };

    private static FtthDropInstallChecklistService Sut() => new(FtthOrderType, CtoRepairOrderType);

    private static ServiceOrder NewOrder() => new()
    {
        Id = "drop-1",
        Title = "Drop install — 123 Main St",
        Status = FtthDropInstallStatus.CrmSale,
        OrganizationId = Guid.NewGuid(),
        CreatedBy = "tech-1",
    };

    [Fact]
    public void FullHappyPath_CompletesAndCarriesEveryCollectedField()
    {
        var sut = Sut();
        var order = NewOrder();

        sut.RecordCtoArrival(order, TimeProvider.System).Succeeded.Should().BeTrue();
        sut.RecordCtoReading(order, -18.0, TimeProvider.System).Succeeded.Should().BeTrue();
        sut.RecordTagScan(order, "TAG-0001", TimeProvider.System).Succeeded.Should().BeTrue();
        sut.RecordPtoReadingAndVerifyBudget(order, -18.3, TimeProvider.System).Succeeded.Should().BeTrue();
        sut.RecordOntScan(order, "HWTC12345678", "AA:BB:CC:DD:EE:FF", TimeProvider.System).Succeeded.Should().BeTrue();

        FtthDropInstallChecklistService.RecordEvidencePhoto(order, FtthEvidenceSlot.CtoConnection);
        FtthDropInstallChecklistService.RecordEvidencePhoto(order, FtthEvidenceSlot.DropAnchorTag);
        FtthDropInstallChecklistService.RecordEvidencePhoto(order, FtthEvidenceSlot.PtoAndOnt);
        FtthDropInstallChecklistService.RecordEvidencePhoto(order, FtthEvidenceSlot.CustomerSignOnGlass);

        var result = sut.TryComplete(order, TimeProvider.System);

        result.Succeeded.Should().BeTrue();
        order.Status.Should().Be(FtthDropInstallStatus.Activated);

        // AC: "Completed checklist payload contains all fields required by the downstream BSS
        // activation sync" — every field the checklist collected must survive to completion.
        order.Attributes.Should().ContainKey(FtthDropInstallChecklistAttributes.CtoReadingDbmKey);
        order.Attributes.Should().ContainKey(FtthDropInstallChecklistAttributes.PtoReadingDbmKey);
        order.Attributes.Should().ContainKey(FtthDropInstallChecklistAttributes.YellowTagIdKey);
        order.Attributes.Should().ContainKey(FtthDropInstallChecklistAttributes.OntFsanKey);
        order.Attributes.Should().ContainKey(FtthDropInstallChecklistAttributes.OntMacAddressKey);
        order.Attributes.Should().ContainKey(FtthDropInstallTransitioner.OpmReadingPassedAttributeKey);
        order.Attributes[FtthDropInstallTransitioner.OpmReadingPassedAttributeKey].Should().Be("true");
        FtthEvidenceCompletenessChecker.IsComplete(order.Attributes).Should().BeTrue();
    }

    [Fact]
    public void RecordTagScan_MalformedPayload_BlocksWithExplanationAndStaysAtDropLaunch()
    {
        var sut = Sut();
        var order = NewOrder();
        sut.RecordCtoArrival(order, TimeProvider.System);
        sut.RecordCtoReading(order, -18.0, TimeProvider.System);

        var result = sut.RecordTagScan(order, "!!", TimeProvider.System);

        result.Succeeded.Should().BeFalse();
        result.Explanation.Should().NotBeNullOrWhiteSpace();
        order.Status.Should().Be(FtthDropInstallStatus.DropLaunch);
    }

    [Fact]
    public void RecordPtoReadingAndVerifyBudget_ExcessiveInsertionLoss_BlocksWithExplanationAndNoRepairOrder()
    {
        var sut = Sut();
        var order = NewOrder();
        sut.RecordCtoArrival(order, TimeProvider.System);
        sut.RecordCtoReading(order, -18.0, TimeProvider.System);
        sut.RecordTagScan(order, "TAG-0001", TimeProvider.System);

        var result = sut.RecordPtoReadingAndVerifyBudget(order, -19.0, TimeProvider.System); // 1.0 dB loss > 0.5 dB budget

        result.Succeeded.Should().BeFalse();
        result.Explanation.Should().Contain("insertion loss");
        result.UpstreamRepairOrder.Should().BeNull();
        order.Status.Should().Be(FtthDropInstallStatus.TagAffixed);
        order.Attributes[FtthDropInstallTransitioner.OpmReadingPassedAttributeKey].Should().Be("false");
    }

    [Fact]
    public void RecordPtoReadingAndVerifyBudget_CtoReadingBelowMinimum_BlocksAndBuildsUpstreamRepairOrder()
    {
        var sut = Sut();
        var order = NewOrder();
        order.Attributes[FtthDropInstallChecklistAttributes.CtoFeatureIdKey] = "cto-1";
        sut.RecordCtoArrival(order, TimeProvider.System);
        sut.RecordCtoReading(order, OpticalBudgetVerificationService.CtoMinDbm - 1.0, TimeProvider.System);
        sut.RecordTagScan(order, "TAG-0001", TimeProvider.System);

        var result = sut.RecordPtoReadingAndVerifyBudget(order, -20.0, TimeProvider.System);

        result.Succeeded.Should().BeFalse();
        result.UpstreamRepairOrder.Should().NotBeNull();
        result.UpstreamRepairOrder!.ParentOrderId.Should().Be(order.Id);
        result.UpstreamRepairOrder.Attributes["ctoFeatureId"].Should().Be("cto-1");
    }

    [Fact]
    public void RecordPtoReadingAndVerifyBudget_NoCtoReadingRecorded_BlocksWithExplanation()
    {
        var sut = Sut();
        var order = NewOrder();
        sut.RecordCtoArrival(order, TimeProvider.System);
        // Skips RecordCtoReading entirely.

        var result = sut.RecordPtoReadingAndVerifyBudget(order, -18.0, TimeProvider.System);

        result.Succeeded.Should().BeFalse();
        result.Explanation.Should().Contain("CTO reading was never recorded");
    }

    [Fact]
    public void RecordOntScan_InvalidMacAddress_BlocksWithExplanation()
    {
        var sut = Sut();
        var order = NewOrder();
        sut.RecordCtoArrival(order, TimeProvider.System);
        sut.RecordCtoReading(order, -18.0, TimeProvider.System);
        sut.RecordTagScan(order, "TAG-0001", TimeProvider.System);
        sut.RecordPtoReadingAndVerifyBudget(order, -18.3, TimeProvider.System);

        var result = sut.RecordOntScan(order, "HWTC12345678", "not-a-mac", TimeProvider.System);

        result.Succeeded.Should().BeFalse();
        order.Status.Should().Be(FtthDropInstallStatus.PtoPower);
    }

    [Fact]
    public void TryComplete_MissingEvidenceSlots_BlocksWithExplanationListingThem()
    {
        var sut = Sut();
        var order = NewOrder();
        sut.RecordCtoArrival(order, TimeProvider.System);
        sut.RecordCtoReading(order, -18.0, TimeProvider.System);
        sut.RecordTagScan(order, "TAG-0001", TimeProvider.System);
        sut.RecordPtoReadingAndVerifyBudget(order, -18.3, TimeProvider.System);
        sut.RecordOntScan(order, "HWTC12345678", "AA:BB:CC:DD:EE:FF", TimeProvider.System);
        FtthDropInstallChecklistService.RecordEvidencePhoto(order, FtthEvidenceSlot.CtoConnection);

        var result = sut.TryComplete(order, TimeProvider.System);

        result.Succeeded.Should().BeFalse();
        result.Explanation.Should().Contain("DropAnchorTag").And.Contain("PtoAndOnt").And.Contain("CustomerSignOnGlass");
        order.Status.Should().Be(FtthDropInstallStatus.OntProvisioned);
    }

    [Fact]
    public void RecordEvidencePhoto_CalledTwiceForSameSlot_IsIdempotent()
    {
        var order = NewOrder();

        FtthDropInstallChecklistService.RecordEvidencePhoto(order, FtthEvidenceSlot.CtoConnection);
        FtthDropInstallChecklistService.RecordEvidencePhoto(order, FtthEvidenceSlot.CtoConnection);

        FtthEvidenceCompletenessChecker.MissingSlots(order.Attributes).Should().HaveCount(3);
    }
}
