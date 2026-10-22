using FluentAssertions;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.IpBilling.Tests;

public class IpBillingWorkflowGraphTests
{
    [Fact]
    public void States_HasAllFourStagesWithValidadoAsTheOnlySuccessState()
    {
        var states = IpBillingWorkflowGraph.States();

        states.Select(s => s.Key).Should().BeEquivalentTo([
            IpBillingWorkflowGraph.Carregado,
            IpBillingWorkflowGraph.Calculado,
            IpBillingWorkflowGraph.Validado,
            IpBillingWorkflowGraph.Cancelado,
        ]);

        states.Single(s => s.IsSuccess).Key.Should().Be(IpBillingWorkflowGraph.Validado);
    }

    [Fact]
    public void Transitions_CarregadoToCalculadoToValidado_AreLegalAndActionTagged()
    {
        var transitions = IpBillingWorkflowGraph.Transitions();

        transitions.Should().ContainSingle(t =>
            t.FromStateKey == IpBillingWorkflowGraph.Carregado &&
            t.ToStateKey == IpBillingWorkflowGraph.Calculado &&
            t.TriggerAction == OrderActionType.Calculate);

        transitions.Should().ContainSingle(t =>
            t.FromStateKey == IpBillingWorkflowGraph.Calculado &&
            t.ToStateKey == IpBillingWorkflowGraph.Validado &&
            t.TriggerAction == OrderActionType.Validate);
    }

    [Fact]
    public void Transitions_CarregadoAndCalculado_CanBothCancel()
    {
        var transitions = IpBillingWorkflowGraph.Transitions();

        transitions.Should().Contain(t =>
            t.FromStateKey == IpBillingWorkflowGraph.Carregado &&
            t.ToStateKey == IpBillingWorkflowGraph.Cancelado &&
            t.TriggerAction == OrderActionType.Cancel);

        transitions.Should().Contain(t =>
            t.FromStateKey == IpBillingWorkflowGraph.Calculado &&
            t.ToStateKey == IpBillingWorkflowGraph.Cancelado &&
            t.TriggerAction == OrderActionType.Cancel);
    }

    [Fact]
    public void States_And_Transitions_ReturnIndependentInstancesEachCall()
    {
        // Documented contract: callers may safely mutate the list they get back without
        // affecting another order type's copy.
        var first  = IpBillingWorkflowGraph.States();
        var second = IpBillingWorkflowGraph.States();

        first.Should().NotBeSameAs(second);
        first.Add(new WorkflowState("Extra", "Extra"));
        second.Should().HaveCount(4);
    }
}
