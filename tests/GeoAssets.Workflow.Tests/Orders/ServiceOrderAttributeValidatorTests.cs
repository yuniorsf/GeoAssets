using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class ServiceOrderAttributeValidatorTests
{
    // Mirrors the emergency-repair schema from the XD01-2 POC: an enum, a regex pattern,
    // a numeric field, a required set, and additionalProperties:false.
    private const string EmergencyRepairSchema = """
    {
      "type": "object",
      "properties": {
        "severity":     { "type": "integer", "minimum": 1, "maximum": 5 },
        "hazard_class": { "type": "string", "enum": ["electrical", "gas", "structural"] },
        "incident_ref": { "type": "string", "pattern": "^INC-[0-9]{6}$" }
      },
      "required": ["severity", "hazard_class"],
      "additionalProperties": false
    }
    """;

    private static OrderType SchemaOrderType(string? schema = EmergencyRepairSchema) => new()
    {
        Id = "emergency-repair",
        DisplayName = "Emergency Repair",
        AttributesSchemaJson = schema,
    };

    private static Dictionary<string, string> ValidAttributes() => new()
    {
        ["severity"] = "3",
        ["hazard_class"] = "gas",
        ["incident_ref"] = "INC-123456",
    };

    // ── Unrestricted (no schema) ─────────────────────────────────────────────

    [Fact]
    public void Validate_NoSchema_AlwaysValid()
    {
        var orderType = new OrderType { Id = "inspection", DisplayName = "Inspection" };

        var errors = ServiceOrderAttributeValidator.Validate(orderType, new Dictionary<string, string> { ["anything"] = "goes" });

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhitespaceSchema_AlwaysValid()
    {
        var orderType = SchemaOrderType(schema: "   ");

        var errors = ServiceOrderAttributeValidator.Validate(orderType, new Dictionary<string, string> { ["anything"] = "goes" });

        errors.Should().BeEmpty();
    }

    // ── The 5 POC cases ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidAttributes_NoErrors()
        => ServiceOrderAttributeValidator.Validate(SchemaOrderType(), ValidAttributes()).Should().BeEmpty();

    [Fact]
    public void Validate_MissingRequired_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs.Remove("hazard_class");

        ServiceOrderAttributeValidator.Validate(SchemaOrderType(), attrs).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_BadEnumValue_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs["hazard_class"] = "lava";

        ServiceOrderAttributeValidator.Validate(SchemaOrderType(), attrs).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_WrongType_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs["severity"] = "not-a-number";

        ServiceOrderAttributeValidator.Validate(SchemaOrderType(), attrs).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_UnknownKey_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs["unexpected"] = "value";

        ServiceOrderAttributeValidator.Validate(SchemaOrderType(), attrs).Should().NotBeEmpty();
    }

    // ── String-value → JSON coercion ─────────────────────────────────────────

    [Fact]
    public void Validate_NumericStringSatisfiesIntegerType()
    {
        var attrs = ValidAttributes();
        attrs["severity"] = "5";

        ServiceOrderAttributeValidator.Validate(SchemaOrderType(), attrs).Should().BeEmpty();
    }

    [Fact]
    public void Validate_OutOfRangeNumberFailsMinimumConstraint()
    {
        var attrs = ValidAttributes();
        attrs["severity"] = "99";

        ServiceOrderAttributeValidator.Validate(SchemaOrderType(), attrs).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_PlainTextFallsBackToJsonString()
    {
        // "Downtown Depot" is not valid JSON on its own; must fall back to a JSON string
        // node rather than throwing, and satisfy a `type: string` + pattern constraint.
        var attrs = ValidAttributes();
        attrs["incident_ref"] = "INC-654321";

        ServiceOrderAttributeValidator.Validate(SchemaOrderType(), attrs).Should().BeEmpty();
    }

    [Fact]
    public void Validate_PatternMismatch_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs["incident_ref"] = "not-a-valid-ref";

        ServiceOrderAttributeValidator.Validate(SchemaOrderType(), attrs).Should().NotBeEmpty();
    }

    // ── EnsureValid ──────────────────────────────────────────────────────────

    [Fact]
    public void EnsureValid_ValidAttributes_DoesNotThrow()
        => FluentActions.Invoking(() => ServiceOrderAttributeValidator.EnsureValid(SchemaOrderType(), ValidAttributes()))
            .Should().NotThrow();

    [Fact]
    public void EnsureValid_InvalidAttributes_ThrowsWithOrderTypeIdAndErrors()
    {
        var attrs = ValidAttributes();
        attrs.Remove("hazard_class");

        var act = () => ServiceOrderAttributeValidator.EnsureValid(SchemaOrderType(), attrs);

        var thrown = act.Should().Throw<ServiceOrderAttributeValidationException>().Which;
        thrown.OrderTypeId.Should().Be("emergency-repair");
        thrown.Errors.Should().NotBeEmpty();
    }
}
