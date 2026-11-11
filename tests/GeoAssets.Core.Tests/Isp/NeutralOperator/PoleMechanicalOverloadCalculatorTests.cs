using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator;

public class PoleMechanicalOverloadCalculatorTests
{
    private static PoleAttachmentSlot Slot(int index, decimal loadDaN, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted) =>
        new() { SlotIndex = index, Status = status, CableLoadDaN = loadDaN };

    private static PoleAttachmentSlotMatrix MatrixOf(params PoleAttachmentSlot[] slots)
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        foreach (var slot in slots) matrix.AddSlot(slot);
        return matrix;
    }

    [Fact]
    public void Evaluate_SummedAuthorizedLoadUnderCapacity_NotOverloaded()
    {
        var matrix = MatrixOf(Slot(1, 200m), Slot(2, 300m));

        var result = PoleMechanicalOverloadCalculator.Evaluate(matrix, ratedCapacityDaN: 600m);

        result.IsOverloaded.Should().BeFalse();
        result.TotalAuthorizedLoadDaN.Should().Be(500m);
        result.RatedCapacityDaN.Should().Be(600m);
    }

    [Fact]
    public void Evaluate_SummedAuthorizedLoadExceedsCapacity_Overloaded()
    {
        var matrix = MatrixOf(Slot(1, 400m), Slot(2, 350m));

        var result = PoleMechanicalOverloadCalculator.Evaluate(matrix, ratedCapacityDaN: 600m);

        result.IsOverloaded.Should().BeTrue();
        result.TotalAuthorizedLoadDaN.Should().Be(750m);
    }

    [Fact]
    public void Evaluate_SummedLoadExactlyAtCapacity_NotOverloaded()
    {
        // The AC says "exceeds" — exactly at the rated limit is not yet an overload.
        var matrix = MatrixOf(Slot(1, 600m));

        var result = PoleMechanicalOverloadCalculator.Evaluate(matrix, ratedCapacityDaN: 600m);

        result.IsOverloaded.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NonAuthorizedSlotsExcludedFromSum()
    {
        // A reservation, an abandoned dead-drop, and a clandestine cable all carry load values,
        // but only AuthorizedContracted attachments count toward the rated-capacity check.
        var matrix = MatrixOf(
            Slot(1, 500m, PoleAttachmentOccupantStatus.AuthorizedContracted),
            Slot(2, 500m, PoleAttachmentOccupantStatus.ActiveReservation),
            Slot(3, 500m, PoleAttachmentOccupantStatus.AbandonedDeadDrop),
            Slot(4, 500m, PoleAttachmentOccupantStatus.ClandestineUnauthorized));

        var result = PoleMechanicalOverloadCalculator.Evaluate(matrix, ratedCapacityDaN: 600m);

        result.TotalAuthorizedLoadDaN.Should().Be(500m);
        result.IsOverloaded.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_EmptyMatrix_NotOverloaded()
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };

        var result = PoleMechanicalOverloadCalculator.Evaluate(matrix, ratedCapacityDaN: 600m);

        result.TotalAuthorizedLoadDaN.Should().Be(0m);
        result.IsOverloaded.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NullMatrix_Throws()
    {
        var act = () => PoleMechanicalOverloadCalculator.Evaluate(null!, ratedCapacityDaN: 600m);

        act.Should().Throw<ArgumentNullException>();
    }
}
