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

    private static Dictionary<string, string> ValidAttributes() => new(StringComparer.Ordinal)
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

        var errors = ServiceOrderAttributeValidator.Validate(orderType, new Dictionary<string, string>(StringComparer.Ordinal) { ["anything"] = "goes" });

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhitespaceSchema_AlwaysValid()
    {
        var orderType = SchemaOrderType(schema: "   ");

        var errors = ServiceOrderAttributeValidator.Validate(orderType, new Dictionary<string, string>(StringComparer.Ordinal) { ["anything"] = "goes" });

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

    // ── ValidateSchemaDocument (XD01-205: pre-validate a candidate schema before persisting it) ──

    [Fact]
    public void ValidateSchemaDocument_ValidDraft202012Schema_NoErrors()
        => ServiceOrderAttributeValidator.ValidateSchemaDocument(EmergencyRepairSchema).Should().BeEmpty();

    [Fact]
    public void ValidateSchemaDocument_ValidSchemaDeclaringDraft202012Dialect_NoErrors()
    {
        const string schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": { "name": { "type": "string" } },
          "required": ["name"]
        }
        """;

        ServiceOrderAttributeValidator.ValidateSchemaDocument(schema).Should().BeEmpty();
    }

    [Fact]
    public void ValidateSchemaDocument_NotJson_HasActionableError()
    {
        var errors = ServiceOrderAttributeValidator.ValidateSchemaDocument("{ this is not json");

        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.Contains("JSON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSchemaDocument_DeclaresOlderDialect_IsRejected()
    {
        const string schema = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object"
        }
        """;

        var errors = ServiceOrderAttributeValidator.ValidateSchemaDocument(schema);

        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.Contains("2020-12", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateSchemaDocument_KeywordWithWrongShape_HasActionableError()
    {
        const string schema = """
        {
          "type": "object",
          "required": "should-be-an-array"
        }
        """;

        var errors = ServiceOrderAttributeValidator.ValidateSchemaDocument(schema);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateSchemaDocument_MetaSchemaViolation_HasActionableError()
    {
        // "minLength" must be a non-negative integer per the draft 2020-12 meta-schema.
        // JsonSchema.FromText's own deserializer already rejects this (see the keyword-shape
        // test above), but this case exists to prove the meta-schema evaluation layer itself
        // is wired in, not just FromText's deserializer validation.
        const string schema = """
        {
          "type": "string",
          "minLength": -5
        }
        """;

        ServiceOrderAttributeValidator.ValidateSchemaDocument(schema).Should().NotBeEmpty();
    }
}
