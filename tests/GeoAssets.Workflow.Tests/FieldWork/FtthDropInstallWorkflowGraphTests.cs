using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>
/// XD01-273's AC: "State machine enforces strict transition order; invalid transitions rejected
/// with a typed error."
/// </summary>
public class FtthDropInstallWorkflowGraphTests
{
    private static readonly OrderType FtthOrderType =
        FtthDropInstallWorkflowGraph.BuildOrderType("ftth-drop-install", "FTTH Drop Install");

    public static readonly TheoryData<string, string> ValidEdges = new()
    {
        { FtthDropInstallStatus.CrmSale, FtthDropInstallStatus.CtoPortTest },
        { FtthDropInstallStatus.CtoPortTest, FtthDropInstallStatus.DropLaunch },
        { FtthDropInstallStatus.DropLaunch, FtthDropInstallStatus.TagAffixed },
        { FtthDropInstallStatus.TagAffixed, FtthDropInstallStatus.PtoPower },
        { FtthDropInstallStatus.PtoPower, FtthDropInstallStatus.OntProvisioned },
        { FtthDropInstallStatus.OntProvisioned, FtthDropInstallStatus.Activated },
    };

    [Theory]
    [MemberData(nameof(ValidEdges))]
    public void IsValid_EveryDocumentedEdge_ReturnsTrue(string from, string to) =>
        ServiceOrderTransitions.IsValid(FtthOrderType, from, to).Should().BeTrue($"'{from}' -> '{to}' is a documented edge of FtthDropInstallWorkflowGraph");

    public static readonly TheoryData<string, string> IllegalEdges = new()
    {
        { FtthDropInstallStatus.CrmSale, FtthDropInstallStatus.PtoPower },
        { FtthDropInstallStatus.CrmSale, FtthDropInstallStatus.Activated },
        { FtthDropInstallStatus.CtoPortTest, FtthDropInstallStatus.CrmSale },
        { FtthDropInstallStatus.DropLaunch, FtthDropInstallStatus.OntProvisioned },
        { FtthDropInstallStatus.Activated, FtthDropInstallStatus.CrmSale },
    };

    [Theory]
    [MemberData(nameof(IllegalEdges))]
    public void IsValid_UndocumentedEdge_ReturnsFalse(string from, string to) =>
        ServiceOrderTransitions.IsValid(FtthOrderType, from, to).Should().BeFalse($"'{from}' -> '{to}' skips or reverses the strict linear chain");

    [Theory]
    [MemberData(nameof(IllegalEdges))]
    public void Transition_UndocumentedEdge_ThrowsInvalidServiceOrderTransitionException(string from, string to)
    {
        var order = new ServiceOrder { Status = from };

        var act = () => order.Transition(to, TimeProvider.System, FtthOrderType);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
        order.Status.Should().Be(from, "a rejected transition must never mutate the order");
    }

    [Fact]
    public void Activated_IsTheOnlySuccessTerminalState() =>
        FtthDropInstallWorkflowGraph.States.Where(s => s.IsSuccess).Select(s => s.Key).Should().BeEquivalentTo([FtthDropInstallStatus.Activated]);

    [Fact]
    public void InitialStateKey_IsCrmSale() =>
        FtthDropInstallWorkflowGraph.InitialStateKey.Should().Be(FtthDropInstallStatus.CrmSale);
}
