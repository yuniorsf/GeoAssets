using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator;

public class PoleSharingComplianceChecklistSchemaTests
{
    private static readonly OrderType PoleCensusOrderType =
        PoleCensusOrderTypes.BuildPoleCensusInspection();

    private static Dictionary<string, string> ValidAttributes() => new(StringComparer.Ordinal)
    {
        ["structuralCondition"] = "Good",
        ["band500mmCompliant"] = "true",
        ["clearanceMeasurements"] = """{"groundToLowestCableMeters": 5.2, "powerToCommunicationBandMeters": 0.5}""",
        ["occupants"] = """[{"slotIndex": 1, "status": "AuthorizedContracted", "operatorCnpj": "12345678000190"}]""",
        ["unauthorizedCableDetected"] = "false",
    };

    [Fact]
    public void ConformantSubmission_IsValid() =>
        ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, ValidAttributes()).Should().BeEmpty();

    [Theory]
    [InlineData("structuralCondition")]
    [InlineData("band500mmCompliant")]
    [InlineData("clearanceMeasurements")]
    [InlineData("occupants")]
    [InlineData("unauthorizedCableDetected")]
    public void MissingRequiredField_IsInvalid(string field)
    {
        var attributes = ValidAttributes();
        attributes.Remove(field);

        ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void StructuralCondition_InvalidEnumValue_IsInvalid()
    {
        var attributes = ValidAttributes();
        attributes["structuralCondition"] = "Excellent";

        ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void Occupants_InvalidStatusEnumValue_IsInvalid()
    {
        var attributes = ValidAttributes();
        attributes["occupants"] = """[{"slotIndex": 1, "status": "NotARealStatus"}]""";

        ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, attributes).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]   // below minimum (1)
    [InlineData(7)]   // above maximum (6)
    public void Occupants_SlotIndexOutOfRange_IsInvalid(int slotIndex)
    {
        var attributes = ValidAttributes();
        attributes["occupants"] = $$"""[{"slotIndex": {{slotIndex}}, "status": "AuthorizedContracted"}]""";

        ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void Occupants_SevenEntries_ExceedsMaxItems_IsInvalid()
    {
        var attributes = ValidAttributes();
        var entries = string.Join(",", Enumerable.Range(1, 7)
            .Select(i => $$"""{"slotIndex": 1, "status": "AuthorizedContracted"}"""));
        attributes["occupants"] = $"[{entries}]";

        ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void Occupants_EmptyArray_IsValid()
    {
        var attributes = ValidAttributes();
        attributes["occupants"] = "[]";

        ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, attributes).Should().BeEmpty(
            "a pole with no observed attachments this visit is still a valid census result");
    }

    [Theory]
    [InlineData("-0.1", "0.5")]  // groundToLowestCableMeters below minimum
    [InlineData("15.1", "0.5")]  // groundToLowestCableMeters above maximum
    [InlineData("5", "-0.1")]    // powerToCommunicationBandMeters below minimum
    [InlineData("5", "2.1")]     // powerToCommunicationBandMeters above maximum
    public void ClearanceMeasurements_OutOfRange_IsInvalid(string groundToLowestCableMeters, string powerToCommunicationBandMeters)
    {
        var attributes = ValidAttributes();
        attributes["clearanceMeasurements"] =
            $$"""{"groundToLowestCableMeters": {{groundToLowestCableMeters}}, "powerToCommunicationBandMeters": {{powerToCommunicationBandMeters}}}""";

        ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, attributes).Should().NotBeEmpty();
    }

    [Fact]
    public void AdditionalUnknownTopLevelProperty_IsInvalid()
    {
        var attributes = ValidAttributes();
        attributes["someUnknownField"] = "true";

        ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, attributes).Should().NotBeEmpty();
    }
}
