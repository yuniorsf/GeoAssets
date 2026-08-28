using FluentAssertions;
using GeoAssets.Shared.Components.Assets;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Assets;

public class SchemaDrivenAttributeEditorTests
{
    // ── Empty / malformed input ───────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseFields_NullOrWhitespace_ReturnsEmpty(string? schema)
    {
        SchemaDrivenAttributeEditor.ParseFields(schema).Should().BeEmpty();
    }

    [Fact]
    public void ParseFields_MalformedJson_ReturnsEmpty()
    {
        SchemaDrivenAttributeEditor.ParseFields("{ not valid json").Should().BeEmpty();
    }

    [Fact]
    public void ParseFields_NoPropertiesObject_ReturnsEmpty()
    {
        SchemaDrivenAttributeEditor.ParseFields("""{ "type": "object" }""").Should().BeEmpty();
    }

    // ── Type dispatch ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseFields_StringType_ReturnsStringKind()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "material": { "type": "string" } } }
            """);

        fields.Should().ContainSingle().Which.Kind.Should().Be(SchemaFieldKind.String);
    }

    [Fact]
    public void ParseFields_NoTypeSpecified_DefaultsToStringKind()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "material": {} } }
            """);

        fields.Should().ContainSingle().Which.Kind.Should().Be(SchemaFieldKind.String);
    }

    [Fact]
    public void ParseFields_IntegerType_ReturnsIntegerKindWithBounds()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "diameter_mm": { "type": "integer", "minimum": 10, "maximum": 500 } } }
            """);

        var field = fields.Should().ContainSingle().Which;
        field.Kind.Should().Be(SchemaFieldKind.Integer);
        field.Minimum.Should().Be(10);
        field.Maximum.Should().Be(500);
    }

    [Fact]
    public void ParseFields_NumberType_ReturnsNumberKind()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "voltage": { "type": "number" } } }
            """);

        fields.Should().ContainSingle().Which.Kind.Should().Be(SchemaFieldKind.Number);
    }

    [Fact]
    public void ParseFields_BooleanType_ReturnsBooleanKind()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "energized": { "type": "boolean" } } }
            """);

        fields.Should().ContainSingle().Which.Kind.Should().Be(SchemaFieldKind.Boolean);
    }

    [Fact]
    public void ParseFields_MissingMinimumOrMaximum_ReturnsNull()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "diameter_mm": { "type": "integer" } } }
            """);

        var field = fields.Should().ContainSingle().Which;
        field.Minimum.Should().BeNull();
        field.Maximum.Should().BeNull();
    }

    // ── Enum ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFields_EnumProperty_ReturnsEnumKindWithValues()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "material": { "type": "string", "enum": ["steel", "wood", "concrete"] } } }
            """);

        var field = fields.Should().ContainSingle().Which;
        field.Kind.Should().Be(SchemaFieldKind.Enum);
        field.EnumValues.Should().Equal("steel", "wood", "concrete");
    }

    [Fact]
    public void ParseFields_EnumProperty_TakesPriorityOverType()
    {
        // "type": "integer" would otherwise map to Integer — enum must win regardless of type.
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "phase": { "type": "integer", "enum": [1, 2, 3] } } }
            """);

        fields.Should().ContainSingle().Which.Kind.Should().Be(SchemaFieldKind.Enum);
    }

    // ── Label (title fallback to key) ─────────────────────────────────────────

    [Fact]
    public void ParseFields_WithTitle_UsesTitleAsLabel()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "diameter_mm": { "type": "integer", "title": "Diameter (mm)" } } }
            """);

        fields.Should().ContainSingle().Which.Label.Should().Be("Diameter (mm)");
    }

    [Fact]
    public void ParseFields_NoTitle_FallsBackToPropertyKeyAsLabel()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "diameter_mm": { "type": "integer" } } }
            """);

        fields.Should().ContainSingle().Which.Label.Should().Be("diameter_mm");
    }

    // ── Required ───────────────────────────────────────────────────────────────

    [Fact]
    public void ParseFields_KeyListedInRequired_IsRequiredTrue()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "diameter_mm": { "type": "integer" } }, "required": ["diameter_mm"] }
            """);

        fields.Should().ContainSingle().Which.Required.Should().BeTrue();
    }

    [Fact]
    public void ParseFields_KeyNotListedInRequired_IsRequiredFalse()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "diameter_mm": { "type": "integer" }, "notes": { "type": "string" } }, "required": ["diameter_mm"] }
            """);

        fields.Should().ContainSingle(f => f.Key == "notes").Which.Required.Should().BeFalse();
    }

    [Fact]
    public void ParseFields_NoRequiredArray_AllFieldsNotRequired()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            { "properties": { "diameter_mm": { "type": "integer" } } }
            """);

        fields.Should().ContainSingle().Which.Required.Should().BeFalse();
    }

    // ── Multiple properties ────────────────────────────────────────────────────

    [Fact]
    public void ParseFields_MultipleProperties_ReturnsAllInDeclarationOrder()
    {
        var fields = SchemaDrivenAttributeEditor.ParseFields("""
            {
              "properties": {
                "material": { "type": "string" },
                "diameter_mm": { "type": "integer" },
                "energized": { "type": "boolean" }
              },
              "required": ["material"]
            }
            """);

        fields.Select(f => f.Key).Should().Equal("material", "diameter_mm", "energized");
        fields.Select(f => f.Kind).Should().Equal(SchemaFieldKind.String, SchemaFieldKind.Integer, SchemaFieldKind.Boolean);
    }
}
