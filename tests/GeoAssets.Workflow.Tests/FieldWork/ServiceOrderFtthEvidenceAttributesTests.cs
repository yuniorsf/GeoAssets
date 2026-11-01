using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class ServiceOrderFtthEvidenceAttributesTests
{
    private static ServiceOrder NewOrder() => new() { Id = "order-1" };

    [Fact]
    public void RecordCapturedSlot_NoneCapturedYet_AddsSlot()
    {
        var order = NewOrder();

        ServiceOrderFtthEvidenceAttributes.RecordCapturedSlot(order, FtthEvidenceSlot.CtoConnection);

        ServiceOrderFtthEvidenceAttributes.ReadCapturedSlots(order.Attributes).Should().BeEquivalentTo([FtthEvidenceSlot.CtoConnection]);
    }

    [Fact]
    public void RecordCapturedSlot_AlreadyCaptured_IsIdempotent()
    {
        var order = NewOrder();
        ServiceOrderFtthEvidenceAttributes.RecordCapturedSlot(order, FtthEvidenceSlot.CtoConnection);

        ServiceOrderFtthEvidenceAttributes.RecordCapturedSlot(order, FtthEvidenceSlot.CtoConnection);

        ServiceOrderFtthEvidenceAttributes.ReadCapturedSlots(order.Attributes).Should().HaveCount(1);
    }

    [Fact]
    public void RecordCapturedSlot_SecondDifferentSlot_AccumulatesBoth()
    {
        var order = NewOrder();
        ServiceOrderFtthEvidenceAttributes.RecordCapturedSlot(order, FtthEvidenceSlot.CtoConnection);

        ServiceOrderFtthEvidenceAttributes.RecordCapturedSlot(order, FtthEvidenceSlot.DropAnchorTag);

        ServiceOrderFtthEvidenceAttributes.ReadCapturedSlots(order.Attributes).Should().BeEquivalentTo(
        [
            FtthEvidenceSlot.CtoConnection,
            FtthEvidenceSlot.DropAnchorTag,
        ]);
    }

    [Fact]
    public void ReadCapturedSlots_NoAttributeSet_ReturnsEmpty() =>
        ServiceOrderFtthEvidenceAttributes.ReadCapturedSlots(NewOrder().Attributes).Should().BeEmpty();
}
