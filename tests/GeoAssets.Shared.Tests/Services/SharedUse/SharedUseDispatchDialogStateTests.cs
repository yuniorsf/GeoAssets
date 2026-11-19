using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Services.SharedUse;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.SharedUse;

/// <summary>
/// The decisions of the shared-use dispatch dialog (XD01-316): which dialog an order gets, when
/// the confirm button is live, and what the dialog warns about (T-UI-16, T-UI-17, T-UI-19).
/// </summary>
public sealed class SharedUseDispatchDialogStateTests
{
    private static SharedUseDispatchPreview Preview(
        int recipients = 3, int newRecipients = 2, SharedUseDispatchBlockReason? blockReason = null,
        int blocked = 0, int unnotifiable = 0, int skipped = 0, bool? canDispatch = null) => new()
        {
            OrderId = "ord-1",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            ChildOrderTypeId = SharedUseOrderTypes.OccupantNoticeId,
            RowVersion = [1],
            StatusAfterDispatch = ServiceOrderStatus.Pending,
            TargetCount = 10,
            BlockedTargetCount = blocked,
            TargetsWithoutActivityCount = blockReason == SharedUseDispatchBlockReason.TargetsWithoutActivity ? 4 : 0,
            UnnotifiablePoleCount = unnotifiable,
            SkippedEntryCount = skipped,
            NewRecipientCount = newRecipients,
            BlockReason = blockReason,
            CanDispatch = canDispatch ?? (blockReason is null && newRecipients > 0),
            Recipients = new PagedResult<SharedUseDispatchRecipient> { Items = [], TotalCount = recipients },
        };

    // ── Which dialog (T-UI-16) ────────────────────────────────────────────────

    [Fact]
    public void IsFanOut_IsTrueOnlyForATypeWhoseTargetPolicyDeclaresAFanOut()
    {
        var targetsOnly = new OrderType { Id = "survey", DisplayName = "Survey", TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] } };

        SharedUseDispatchDialogState.IsFanOut(SharedUseOrderTypes.CreatePoleWork()).Should().BeTrue();
        SharedUseDispatchDialogState.IsFanOut(SharedUseOrderTypes.CreateOccupantNotice()).Should().BeFalse("a notice is dispatched the ordinary way, if at all");
        SharedUseDispatchDialogState.IsFanOut(targetsOnly).Should().BeFalse();
        SharedUseDispatchDialogState.IsFanOut(new OrderType { Id = "plain", DisplayName = "Plain" }).Should().BeFalse();
        SharedUseDispatchDialogState.IsFanOut(null).Should().BeFalse("an order whose type the client does not know keeps the ordinary dialog");
    }

    // ── Confirm (T-UI-17, T-UI-19) ────────────────────────────────────────────

    [Fact]
    public void CanConfirm_ADispatchablePlanWithSomeoneNewToNotify_AndNothingRunning_IsTrue() =>
        SharedUseDispatchDialogState.CanConfirm(Preview(), busy: false).Should().BeTrue();

    [Fact]
    public void CanConfirm_WhileARequestIsRunning_IsFalse_SoASecondClickSendsNothing() =>
        SharedUseDispatchDialogState.CanConfirm(Preview(), busy: true).Should().BeFalse();

    [Fact]
    public void CanConfirm_BeforeThePlanIsLoaded_IsFalse() =>
        SharedUseDispatchDialogState.CanConfirm(null, busy: false).Should().BeFalse();

    [Theory]
    [InlineData(SharedUseDispatchBlockReason.NoTargets)]
    [InlineData(SharedUseDispatchBlockReason.TargetsWithoutActivity)]
    [InlineData(SharedUseDispatchBlockReason.AllTargetsBlocked)]
    [InlineData(SharedUseDispatchBlockReason.NoEligibleOccupants)]
    public void CanConfirm_ABlockedPlan_IsFalse_WhateverElseItSays(SharedUseDispatchBlockReason reason) =>
        SharedUseDispatchDialogState.CanConfirm(Preview(blockReason: reason, canDispatch: true), busy: false).Should().BeFalse();

    [Fact]
    public void CanConfirm_EveryOccupantAlreadyDispatched_IsFalse_AndThatIsReportedAsNothingNew()
    {
        var preview = Preview(recipients: 3, newRecipients: 0);

        SharedUseDispatchDialogState.CanConfirm(preview, busy: false).Should().BeFalse();
        SharedUseDispatchDialogState.HasNothingNew(preview).Should().BeTrue();
    }

    [Fact]
    public void CanConfirm_TheHostSayingItCannotDispatch_IsFalse_EvenWithNewRecipientsCounted() =>
        SharedUseDispatchDialogState.CanConfirm(Preview(newRecipients: 2, canDispatch: false), busy: false).Should().BeFalse();

    [Fact]
    public void CanConfirm_APlanThatOnlyLeavesThingsOut_IsStillTrue_ItWarnsButDoesNotStop() =>
        SharedUseDispatchDialogState.CanConfirm(Preview(blocked: 1, unnotifiable: 2, skipped: 5), busy: false).Should().BeTrue();

    [Fact]
    public void HasNothingNew_IsFalse_WhileThereIsSomeoneNew_WhenThePlanIsBlocked_WithNoRecipient_AndWithNoPlan()
    {
        SharedUseDispatchDialogState.HasNothingNew(Preview(newRecipients: 1)).Should().BeFalse();
        SharedUseDispatchDialogState.HasNothingNew(Preview(recipients: 0, newRecipients: 0, blockReason: SharedUseDispatchBlockReason.NoEligibleOccupants)).Should().BeFalse();
        SharedUseDispatchDialogState.HasNothingNew(Preview(recipients: 0, newRecipients: 0)).Should().BeFalse();
        SharedUseDispatchDialogState.HasNothingNew(null).Should().BeFalse();
    }

    // ── Caveats ───────────────────────────────────────────────────────────────

    [Fact]
    public void Caveats_APlanThatLeavesNothingOut_HasNone_AndNoPlanHasNone()
    {
        SharedUseDispatchDialogState.Caveats(Preview()).Should().BeEmpty();
        SharedUseDispatchDialogState.Caveats(null).Should().BeEmpty();
    }

    [Fact]
    public void Caveats_ListWhatIsLeftOut_WithItsCount_InAFixedOrder()
    {
        var caveats = SharedUseDispatchDialogState.Caveats(Preview(blocked: 1, unnotifiable: 2, skipped: 5));

        caveats.Should().Equal(
            new SharedUseDispatchCaveat("sharedUse.caveat.blocked", 1),
            new SharedUseDispatchCaveat("sharedUse.caveat.unnotifiable", 2),
            new SharedUseDispatchCaveat("sharedUse.caveat.skipped", 5));
        caveats.Select(c => c.MessageKey).Should().BeSubsetOf(SharedUseDispatchDialogState.CaveatKeys);
    }

    [Theory]
    [InlineData(1, 0, 0, "sharedUse.caveat.blocked")]
    [InlineData(0, 1, 0, "sharedUse.caveat.unnotifiable")]
    [InlineData(0, 0, 1, "sharedUse.caveat.skipped")]
    public void Caveats_EachKind_AppearsOnItsOwn(int blocked, int unnotifiable, int skipped, string expectedKey) =>
        SharedUseDispatchDialogState.Caveats(Preview(blocked: blocked, unnotifiable: unnotifiable, skipped: skipped))
            .Should().ContainSingle().Which.MessageKey.Should().Be(expectedKey);

    // ── Activity labels ───────────────────────────────────────────────────────

    [Fact]
    public void TargetActivityLabels_UseTheCatalogsNames_InTheTargetsOrder_AndShowAnUnknownIdAsItIs()
    {
        var catalog = SharedUseOrderTypes.Activities;

        TargetActivityLabels.Describe(["remove-attachment", "RELOCATE-ATTACHMENT"], catalog).Should().Be("Remove attachment, Relocate attachment");
        TargetActivityLabels.Describe(["remove-attachment", "paint"], catalog).Should().Be("Remove attachment, paint");
        TargetActivityLabels.Describe(["paint"], catalog: null).Should().Be("paint");
        TargetActivityLabels.Describe([], catalog).Should().Be(TargetActivityLabels.None);
        TargetActivityLabels.Describe(null, catalog).Should().Be(TargetActivityLabels.None);
    }
}
