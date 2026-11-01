using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class ConcessionaireSafetyChecklistSchemasTests
{
    private static readonly OrderType ElectricalOrderType =
        ConcessionaireSafetyChecklistSchemas.BuildElectricalOrderType("field-work-electrical", "Electrical Field Work");

    private static readonly OrderType SanitationOrderType =
        ConcessionaireSafetyChecklistSchemas.BuildSanitationOrderType("field-work-sanitation", "Sanitation Field Work");

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

    private static Dictionary<string, string> ValidSanitationAttributes() => new(StringComparer.Ordinal)
    {
        ["entryPermitIssued"] = "true",
        ["continuousVentilationActive"] = "true",
        ["rescuePlanInPlace"] = "true",
        ["standbyAttendantPresent"] = "true",
        ["communicationEquipmentVerified"] = "true",
        ["oxygenPercent"] = "20.9",
        ["lowerExplosiveLimitPercent"] = "0",
        ["carbonMonoxidePpm"] = "0",
        ["hydrogenSulfidePpm"] = "0",
    };

    // ── Electrical APR (NR-10/NR-35) ─────────────────────────────────────────

    [Fact]
    public void Electrical_AllGatesTrue_IsValid() =>
        ServiceOrderAttributeValidator.Validate(ElectricalOrderType, AllTrueElectricalAttributes()).Should().BeEmpty();

    [Theory]
    [InlineData("circuitDeenergized")]
    [InlineData("lockoutTagoutApplied")]
    [InlineData("voltageAbsenceVerified")]
    [InlineData("groundingApplied")]
    [InlineData("ppeInspected")]
    [InlineData("workAreaIsolated")]
    [InlineData("fallProtectionAnchored")]
    [InlineData("riskAnalysisSignedByCrew")]
    public void Electrical_AnyGateFalse_IsInvalid(string gate)
    {
        var attributes = AllTrueElectricalAttributes();
        attributes[gate] = "false";

        ServiceOrderAttributeValidator.Validate(ElectricalOrderType, attributes).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("circuitDeenergized")]
    [InlineData("riskAnalysisSignedByCrew")]
    public void Electrical_AnyGateMissingEntirely_IsInvalid(string gate)
    {
        var attributes = AllTrueElectricalAttributes();
        attributes.Remove(gate);

        ServiceOrderAttributeValidator.Validate(ElectricalOrderType, attributes).Should().NotBeEmpty();
    }

    // ── Sanitation NR-33 (confined space) ─────────────────────────────────────

    [Fact]
    public void Sanitation_AllGatesTrueAndGasesInRange_IsValid() =>
        ServiceOrderAttributeValidator.Validate(SanitationOrderType, ValidSanitationAttributes()).Should().BeEmpty();

    [Theory]
    [InlineData("oxygenPercent", "19.5")] // lower boundary, inclusive
    [InlineData("oxygenPercent", "23.0")] // upper boundary, inclusive
    [InlineData("lowerExplosiveLimitPercent", "10")]
    [InlineData("carbonMonoxidePpm", "35")]
    [InlineData("hydrogenSulfidePpm", "10")]
    public void Sanitation_GasReadingExactlyAtBoundary_IsValid(string field, string boundaryValue)
    {
        var attributes = ValidSanitationAttributes();
        attributes[field] = boundaryValue;

        ServiceOrderAttributeValidator.Validate(SanitationOrderType, attributes).Should().BeEmpty(
            $"the boundary value itself must pass, matching this codebase's other boundary-inclusive spatial/numeric predicates");
    }

    [Theory]
    [InlineData("oxygenPercent", "19.4")] // just under the lower boundary
    [InlineData("oxygenPercent", "23.1")] // just over the upper boundary
    [InlineData("lowerExplosiveLimitPercent", "10.1")]
    [InlineData("carbonMonoxidePpm", "35.1")]
    [InlineData("hydrogenSulfidePpm", "10.1")]
    public void Sanitation_GasReadingJustOutsideRange_IsInvalid(string field, string outOfRangeValue)
    {
        var attributes = ValidSanitationAttributes();
        attributes[field] = outOfRangeValue;

        ServiceOrderAttributeValidator.Validate(SanitationOrderType, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void Sanitation_AnyBooleanGateFalse_IsInvalid()
    {
        var attributes = ValidSanitationAttributes();
        attributes["rescuePlanInPlace"] = "false";

        ServiceOrderAttributeValidator.Validate(SanitationOrderType, attributes).Should().NotBeEmpty();
    }
}
