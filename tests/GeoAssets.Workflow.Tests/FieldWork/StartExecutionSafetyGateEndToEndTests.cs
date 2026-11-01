using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>
/// XD01-222's stated ACs, proven end-to-end through the real
/// <see cref="FieldWorkOrderTransitioner"/>/<see cref="CompositeFieldWorkTransitionGuard"/> path
/// a real host would use — not just the schema or the guard in isolation.
/// </summary>
public class StartExecutionSafetyGateEndToEndTests
{
    private static readonly OrderType ElectricalOrderType =
        ConcessionaireSafetyChecklistSchemas.BuildElectricalOrderType("field-work-electrical", "Electrical Field Work");

    private static Dictionary<string, string> AllTrueElectricalAttributes() => new(StringComparer.Ordinal)
    {
        ["circuitDeenergized"] = "true",
        ["lockoutTagoutApplied"] = "true",
        ["voltageAbsenceVerified"] = "true",
        ["groundingApplied"] = "true",
        ["ppeInspected"] = "true",
        ["workAreaIsolated"] = "true",
        ["fallProtectionAnchored"] = "true",
        ["riskAnalysisSignedByCrew"] = "true",
    };

    private static CompositeFieldWorkTransitionGuard BuildGuard()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(ElectricalOrderType);
        return new CompositeFieldWorkTransitionGuard(
            NullFieldWorkTransitionGuard.Instance,
            new SafetyChecklistTransitionGuard(registry));
    }

    [Fact]
    public async Task StartExecution_NoPassingSubmission_IsRejected()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite, OrderTypeId = ElectricalOrderType.Id };
        var guard = BuildGuard();

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, ElectricalOrderType, guard);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        order.Status.Should().Be(FieldWorkStatus.OnSite);
    }

    [Fact]
    public async Task StartExecution_SchemaConformantSubmissionExists_IsAccepted()
    {
        var order = new ServiceOrder
        {
            Status = FieldWorkStatus.OnSite,
            OrderTypeId = ElectricalOrderType.Id,
            Attributes = AllTrueElectricalAttributes(),
        };
        var guard = BuildGuard();

        await FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, ElectricalOrderType, guard);

        order.Status.Should().Be(FieldWorkStatus.InProgress);
    }

    [Fact]
    public Task StartExecution_PartialSubmission_IsRejected()
    {
        var attributes = AllTrueElectricalAttributes();
        attributes["ppeInspected"] = "false";
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite, OrderTypeId = ElectricalOrderType.Id, Attributes = attributes };
        var guard = BuildGuard();

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, ElectricalOrderType, guard);

        return act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }
}
