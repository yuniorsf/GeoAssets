using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class SafetyChecklistTransitionGuardTests
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

    private static OrderTypeRegistry RegistryWith(OrderType orderType)
    {
        var registry = new OrderTypeRegistry();
        registry.Register(orderType);
        return registry;
    }

    [Fact]
    public async Task CanStartWorkAsync_NoPassingChecklistSubmission_ReturnsFalse()
    {
        var guard = new SafetyChecklistTransitionGuard(RegistryWith(ElectricalOrderType));
        var order = new ServiceOrder { OrderTypeId = ElectricalOrderType.Id };

        (await guard.CanStartWorkAsync(order)).Should().BeFalse();
    }

    [Fact]
    public async Task CanStartWorkAsync_PassingChecklistSubmissionAttached_ReturnsTrue()
    {
        var guard = new SafetyChecklistTransitionGuard(RegistryWith(ElectricalOrderType));
        var order = new ServiceOrder { OrderTypeId = ElectricalOrderType.Id, Attributes = AllTrueElectricalAttributes() };

        (await guard.CanStartWorkAsync(order)).Should().BeTrue();
    }

    [Fact]
    public async Task CanStartWorkAsync_PartialChecklistSubmission_ReturnsFalse()
    {
        var guard = new SafetyChecklistTransitionGuard(RegistryWith(ElectricalOrderType));
        var attributes = AllTrueElectricalAttributes();
        attributes.Remove("groundingApplied");
        var order = new ServiceOrder { OrderTypeId = ElectricalOrderType.Id, Attributes = attributes };

        (await guard.CanStartWorkAsync(order)).Should().BeFalse();
    }

    [Fact]
    public async Task CanStartWorkAsync_OrderTypeNotRegistered_IsUnrestricted()
    {
        var guard = new SafetyChecklistTransitionGuard(new OrderTypeRegistry());
        var order = new ServiceOrder { OrderTypeId = "not-registered" };

        (await guard.CanStartWorkAsync(order)).Should().BeTrue(
            "the safety gate only applies to order types that actually carry a checklist schema");
    }

    [Fact]
    public async Task CanArriveAsync_AlwaysReturnsTrue_PendingXD01_221Composition()
    {
        var guard = new SafetyChecklistTransitionGuard(new OrderTypeRegistry());

        (await guard.CanArriveAsync(new ServiceOrder(), new GeoPoint(0, 0))).Should().BeTrue();
    }
}
