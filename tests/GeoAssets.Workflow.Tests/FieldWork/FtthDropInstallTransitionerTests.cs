using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>
/// XD01-273's AC: "Entering 'PTO Power' state requires a passing CTO OPM reading already
/// recorded."
/// </summary>
public class FtthDropInstallTransitionerTests
{
    private static readonly OrderType FtthOrderType =
        FtthDropInstallWorkflowGraph.BuildOrderType("ftth-drop-install", "FTTH Drop Install");

    [Fact]
    public void Transition_TagAffixedToPtoPower_NoOpmReadingRecorded_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = FtthDropInstallStatus.TagAffixed };

        var act = () => FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.PtoPower, TimeProvider.System, FtthOrderType);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
        order.Status.Should().Be(FtthDropInstallStatus.TagAffixed, "a rejected transition must never mutate the order");
    }

    [Fact]
    public void Transition_TagAffixedToPtoPower_OpmReadingRecordedAsFailed_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder
        {
            Status = FtthDropInstallStatus.TagAffixed,
            Attributes = { [FtthDropInstallTransitioner.OpmReadingPassedAttributeKey] = "false" },
        };

        var act = () => FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.PtoPower, TimeProvider.System, FtthOrderType);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public void Transition_TagAffixedToPtoPower_PassingOpmReadingRecorded_Succeeds()
    {
        var order = new ServiceOrder
        {
            Status = FtthDropInstallStatus.TagAffixed,
            Attributes = { [FtthDropInstallTransitioner.OpmReadingPassedAttributeKey] = "true" },
        };

        FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.PtoPower, TimeProvider.System, FtthOrderType);

        order.Status.Should().Be(FtthDropInstallStatus.PtoPower);
    }

    [Fact]
    public void Transition_UnguardedEdge_IgnoresMissingOpmReading()
    {
        var order = new ServiceOrder { Status = FtthDropInstallStatus.CrmSale };

        FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.CtoPortTest, TimeProvider.System, FtthOrderType);

        order.Status.Should().Be(FtthDropInstallStatus.CtoPortTest);
    }

    [Fact]
    public void Transition_StructurallyIllegalEdge_ThrowsEvenWithPassingOpmReading()
    {
        var order = new ServiceOrder
        {
            Status = FtthDropInstallStatus.CrmSale,
            Attributes = { [FtthDropInstallTransitioner.OpmReadingPassedAttributeKey] = "true" },
        };

        var act = () => FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.PtoPower, TimeProvider.System, FtthOrderType);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
    }
}
