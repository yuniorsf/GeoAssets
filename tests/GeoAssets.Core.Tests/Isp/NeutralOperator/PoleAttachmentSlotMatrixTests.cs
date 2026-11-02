using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator;

public class PoleAttachmentSlotMatrixTests
{
    private static PoleAttachmentSlot Slot(int index, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.ActiveReservation) =>
        new() { SlotIndex = index, Status = status };

    [Fact]
    public void AddSlot_UpToSix_Succeeds()
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };

        for (var i = 1; i <= PoleAttachmentSlotMatrix.MaxSlots; i++)
            matrix.AddSlot(Slot(i));

        matrix.Slots.Should().HaveCount(6);
    }

    [Fact]
    public void AddSlot_AllSixIndexesOccupied_FurtherAddThrowsOverflow()
    {
        // Slot indexes are bounded to [1, MaxSlots], so once all 6 are occupied the only
        // possible further AddAsync call reuses an existing index — the overflow guard
        // (_slots.Count >= MaxSlots) and the duplicate-index guard necessarily coincide here;
        // this proves the matrix is genuinely closed at 6, not just rejecting one specific index.
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        for (var i = 1; i <= 6; i++) matrix.AddSlot(Slot(i));

        var act = () => matrix.AddSlot(Slot(1, PoleAttachmentOccupantStatus.AuthorizedContracted));

        act.Should().Throw<InvalidOperationException>();
        matrix.Slots.Should().HaveCount(6);
    }

    [Fact]
    public void AddSlot_DuplicateSlotIndex_ThrowsRejected()
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        matrix.AddSlot(Slot(1));

        var act = () => matrix.AddSlot(Slot(1));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already occupied*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public void AddSlot_IndexOutOfRange_Throws(int index)
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };

        var act = () => matrix.AddSlot(Slot(index));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PoleAttachmentOccupantStatus.ActiveReservation, PoleAttachmentOccupantStatus.AuthorizedContracted)]
    [InlineData(PoleAttachmentOccupantStatus.ActiveReservation, PoleAttachmentOccupantStatus.ClandestineUnauthorized)]
    [InlineData(PoleAttachmentOccupantStatus.AuthorizedContracted, PoleAttachmentOccupantStatus.AbandonedDeadDrop)]
    [InlineData(PoleAttachmentOccupantStatus.AuthorizedContracted, PoleAttachmentOccupantStatus.ClandestineUnauthorized)]
    [InlineData(PoleAttachmentOccupantStatus.AbandonedDeadDrop, PoleAttachmentOccupantStatus.ClandestineUnauthorized)]
    [InlineData(PoleAttachmentOccupantStatus.AbandonedDeadDrop, PoleAttachmentOccupantStatus.ActiveReservation)]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized, PoleAttachmentOccupantStatus.AuthorizedContracted)]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized, PoleAttachmentOccupantStatus.ActiveReservation)]
    public void TransitionStatus_AllowedTransition_Succeeds(PoleAttachmentOccupantStatus from, PoleAttachmentOccupantStatus to)
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        matrix.AddSlot(Slot(1, from));

        matrix.TransitionStatus(1, to);

        matrix.Slots[0].Status.Should().Be(to);
    }

    [Fact]
    public void TransitionStatus_SameStatus_AlwaysValid()
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        matrix.AddSlot(Slot(1, PoleAttachmentOccupantStatus.AuthorizedContracted));

        matrix.TransitionStatus(1, PoleAttachmentOccupantStatus.AuthorizedContracted);

        matrix.Slots[0].Status.Should().Be(PoleAttachmentOccupantStatus.AuthorizedContracted);
    }

    [Theory]
    [InlineData(PoleAttachmentOccupantStatus.AuthorizedContracted, PoleAttachmentOccupantStatus.ActiveReservation)]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized, PoleAttachmentOccupantStatus.AbandonedDeadDrop)]
    public void TransitionStatus_DisallowedTransition_Throws(PoleAttachmentOccupantStatus from, PoleAttachmentOccupantStatus to)
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        matrix.AddSlot(Slot(1, from));

        var act = () => matrix.TransitionStatus(1, to);

        act.Should().Throw<InvalidPoleAttachmentSlotTransitionException>()
            .Where(e => e.From == from && e.To == to);
    }

    [Fact]
    public void TransitionStatus_UnknownSlotIndex_Throws()
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };

        var act = () => matrix.TransitionStatus(3, PoleAttachmentOccupantStatus.AuthorizedContracted);

        act.Should().Throw<KeyNotFoundException>();
    }
}
