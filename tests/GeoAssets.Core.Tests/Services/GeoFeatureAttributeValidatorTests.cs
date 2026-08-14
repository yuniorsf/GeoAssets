using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class GeoFeatureAttributeValidatorTests
{
    // Mirrors the emergency-repair schema used by ServiceOrderAttributeValidatorTests (XD01-2):
    // an enum, a regex pattern, a numeric field, a required set, and additionalProperties:false.
    private const string HydrantSchema = """
    {
      "type": "object",
      "properties": {
        "diameter_mm":  { "type": "integer", "minimum": 50, "maximum": 300 },
        "material":     { "type": "string", "enum": ["cast-iron", "pvc", "steel"] },
        "asset_tag":    { "type": "string", "pattern": "^HYD-[0-9]{5}$" }
      },
      "required": ["diameter_mm", "material"],
      "additionalProperties": false
    }
    """;

    private static AssetType SchemaAssetType(string? schema = HydrantSchema) => new()
    {
        Name = "Hydrant",
        AttributesSchemaJson = schema,
    };

    private static Dictionary<string, string> ValidAttributes() => new()
    {
        ["diameter_mm"] = "100",
        ["material"] = "cast-iron",
        ["asset_tag"] = "HYD-12345",
    };

    // ── Unrestricted (no schema) ─────────────────────────────────────────────

    [Fact]
    public void Validate_NoSchema_AlwaysValid()
    {
        var assetType = new AssetType { Name = "Point" };

        var errors = GeoFeatureAttributeValidator.Validate(assetType, new Dictionary<string, string> { ["anything"] = "goes" });

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhitespaceSchema_AlwaysValid()
    {
        var assetType = SchemaAssetType(schema: "   ");

        var errors = GeoFeatureAttributeValidator.Validate(assetType, new Dictionary<string, string> { ["anything"] = "goes" });

        errors.Should().BeEmpty();
    }

    // ── The 5 XD01-2-equivalent cases ────────────────────────────────────────

    [Fact]
    public void Validate_ValidAttributes_NoErrors()
        => GeoFeatureAttributeValidator.Validate(SchemaAssetType(), ValidAttributes()).Should().BeEmpty();

    [Fact]
    public void Validate_MissingRequired_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs.Remove("material");

        GeoFeatureAttributeValidator.Validate(SchemaAssetType(), attrs).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_BadEnumValue_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs["material"] = "wood";

        GeoFeatureAttributeValidator.Validate(SchemaAssetType(), attrs).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_WrongType_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs["diameter_mm"] = "not-a-number";

        GeoFeatureAttributeValidator.Validate(SchemaAssetType(), attrs).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_UnknownKey_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs["unexpected"] = "value";

        GeoFeatureAttributeValidator.Validate(SchemaAssetType(), attrs).Should().NotBeEmpty();
    }

    // ── String-value → JSON coercion ─────────────────────────────────────────

    [Fact]
    public void Validate_NumericStringSatisfiesIntegerType()
    {
        var attrs = ValidAttributes();
        attrs["diameter_mm"] = "150";

        GeoFeatureAttributeValidator.Validate(SchemaAssetType(), attrs).Should().BeEmpty();
    }

    [Fact]
    public void Validate_OutOfRangeNumberFailsMaximumConstraint()
    {
        var attrs = ValidAttributes();
        attrs["diameter_mm"] = "9999";

        GeoFeatureAttributeValidator.Validate(SchemaAssetType(), attrs).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_PlainTextFallsBackToJsonString()
    {
        // "HYD-12345" is not valid JSON on its own; must fall back to a JSON string
        // node rather than throwing, and satisfy a `type: string` + pattern constraint.
        var attrs = ValidAttributes();
        attrs["asset_tag"] = "HYD-54321";

        GeoFeatureAttributeValidator.Validate(SchemaAssetType(), attrs).Should().BeEmpty();
    }

    [Fact]
    public void Validate_PatternMismatch_HasErrors()
    {
        var attrs = ValidAttributes();
        attrs["asset_tag"] = "not-a-valid-tag";

        GeoFeatureAttributeValidator.Validate(SchemaAssetType(), attrs).Should().NotBeEmpty();
    }

    // ── EnsureValid ──────────────────────────────────────────────────────────

    [Fact]
    public void EnsureValid_ValidAttributes_DoesNotThrow()
        => FluentActions.Invoking(() => GeoFeatureAttributeValidator.EnsureValid(SchemaAssetType(), ValidAttributes()))
            .Should().NotThrow();

    [Fact]
    public void EnsureValid_InvalidAttributes_ThrowsWithAssetTypeIdAndErrors()
    {
        var assetType = SchemaAssetType();
        var attrs = ValidAttributes();
        attrs.Remove("material");

        var act = () => GeoFeatureAttributeValidator.EnsureValid(assetType, attrs);

        var thrown = act.Should().Throw<GeoFeatureAttributeValidationException>().Which;
        thrown.AssetTypeId.Should().Be(assetType.Id);
        thrown.Errors.Should().NotBeEmpty();
    }
}
