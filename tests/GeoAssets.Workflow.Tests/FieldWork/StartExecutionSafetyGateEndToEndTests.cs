using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>
/// XD01-222's stated ACs, proven end-to-end through the real
/// <see cref="FieldWorkOrderTransitioner"/>/<see cref="CompositeFieldWorkTransitionGuard"/> path
/// a real host would use — not just the schema or the guard in isolation. Extended by XD01-245
/// (NR-35 pole-structural-integrity gate, timestamped submission, and the explicit
/// "direct API call bypass is rejected server-side" acceptance criterion) using the same
/// end-to-end path, since XD01-245 only widens the existing schema/guard rather than adding a
/// new mechanism.
/// </summary>
public class StartExecutionSafetyGateEndToEndTests
{
    private static readonly OrderType ElectricalOrderType =
        SafetyChecklistSchemas.BuildElectricalOrderType("field-work-electrical", "Electrical Field Work");

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
        ["nr35_pole_structural_integrity"] = "true",
        ["nr35_weatherConditionsAcceptable"] = "true",
        ["aprSignedAtUtc"] = "2026-11-01T14:32:00Z",
    };

    private static CompositeFieldWorkTransitionGuard BuildGuard()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(ElectricalOrderType);
        return new CompositeFieldWorkTransitionGuard(
            NullFieldWorkTransitionGuard.Instance,
            new SafetyChecklistTransitionGuard(registry),
            NullFieldWorkTransitionGuard.Instance,
            NullFieldWorkTransitionGuard.Instance);
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

    // ── XD01-245: NR-35 pole-structural-integrity gate + timestamped submission ─────────────

    [Fact]
    public Task StartExecution_Nr35PoleStructuralIntegrityNotConfirmed_IsRejected()
    {
        var attributes = AllTrueElectricalAttributes();
        attributes["nr35_pole_structural_integrity"] = "false";
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite, OrderTypeId = ElectricalOrderType.Id, Attributes = attributes };
        var guard = BuildGuard();

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, ElectricalOrderType, guard);

        return act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public Task StartExecution_AprSubmissionMissingTimestamp_IsRejected()
    {
        var attributes = AllTrueElectricalAttributes();
        attributes.Remove("aprSignedAtUtc");
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite, OrderTypeId = ElectricalOrderType.Id, Attributes = attributes };
        var guard = BuildGuard();

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, ElectricalOrderType, guard);

        return act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public async Task StartExecution_FullNr10Nr35SubmissionWithTimestamp_IsAccepted()
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

    /// <summary>
    /// Proves XD01-245's third AC directly: a caller that skips the mobile APR checklist UI
    /// entirely and attempts the <see cref="FieldWorkStatus.OnSite"/> → <see cref="FieldWorkStatus.InProgress"/>
    /// transition as a bare API call (no <see cref="ServiceOrder.Attributes"/> submitted at all)
    /// is rejected by <see cref="FieldWorkOrderTransitioner"/> itself — the same server-side path
    /// every caller (UI or otherwise) must go through, so there is no separate code path a
    /// bypass could take.
    /// </summary>
    [Fact]
    public async Task StartExecution_DirectApiCallWithNoApprSubmission_IsRejectedServerSide()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite, OrderTypeId = ElectricalOrderType.Id };
        var guard = BuildGuard();

        var act = () => FieldWorkOrderTransitioner.TransitionAsync(order, FieldWorkStatus.InProgress, TimeProvider.System, ElectricalOrderType, guard);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        order.Status.Should().Be(FieldWorkStatus.OnSite);
    }
}
