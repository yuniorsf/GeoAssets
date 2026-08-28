using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Assets;

public partial class SchemaDrivenAttributeEditor
{
    [Parameter, EditorRequired] public string Schema { get; set; } = string.Empty;
    [Parameter] public Dictionary<string, string> Attributes { get; set; } = [];
    [Parameter] public IReadOnlyList<string> Errors { get; set; } = [];

    private IReadOnlyList<SchemaField> Fields { get; set; } = [];

    protected override void OnParametersSet() => Fields = ParseFields(Schema);

    private string GetValue(string key) => Attributes.TryGetValue(key, out var v) ? v : string.Empty;

    private void SetValue(string key, string value) => Attributes[key] = value;

    private bool IsChecked(string key) => bool.TryParse(GetValue(key), out var b) && b;

    private IReadOnlyList<string> ErrorsFor(SchemaField field) =>
        [.. Errors.Where(e => e.Contains(field.Key, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Parses the top-level <c>properties</c> of a JSON Schema (draft 2020-12) document into
    /// renderable fields: <c>string</c> → text, <c>integer</c>/<c>number</c> → numeric (with
    /// <c>minimum</c>/<c>maximum</c> when present), <c>boolean</c> → checkbox, and any property
    /// carrying an <c>enum</c> → select (regardless of its <c>type</c>). <c>title</c> becomes the
    /// field label, falling back to the property key. Returns an empty list for a null/empty/malformed
    /// schema or one with no <c>properties</c> object — the same "unrestricted by default" convention
    /// as <see cref="GeoAssets.Core.Services.GeoFeatureAttributeValidator"/>, and defensive against a
    /// schema that reaches here without having gone through the save-time syntax check in
    /// <c>AssetTypeManager</c> (e.g. seeded directly).
    /// </summary>
    public static IReadOnlyList<SchemaField> ParseFields(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson)) return [];

        JsonDocument doc;
        try { doc = JsonDocument.Parse(schemaJson); }
        catch (JsonException) { return []; }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
                return [];

            var required = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("required", out var requiredArray) && requiredArray.ValueKind == JsonValueKind.Array)
                foreach (var r in requiredArray.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String)
                        required.Add(r.GetString()!);

            var fields = new List<SchemaField>();
            foreach (var prop in properties.EnumerateObject())
            {
                var key = prop.Name;
                var schema = prop.Value;

                var label = schema.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                    ? titleEl.GetString()!
                    : key;

                IReadOnlyList<string>? enumValues = null;
                if (schema.TryGetProperty("enum", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array)
                    enumValues = [.. enumEl.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())];

                var kind = enumValues is not null
                    ? SchemaFieldKind.Enum
                    : schema.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                        ? typeEl.GetString() switch
                        {
                            "integer" => SchemaFieldKind.Integer,
                            "number" => SchemaFieldKind.Number,
                            "boolean" => SchemaFieldKind.Boolean,
                            _ => SchemaFieldKind.String
                        }
                        : SchemaFieldKind.String;

                var minimum = schema.TryGetProperty("minimum", out var minEl) && minEl.ValueKind == JsonValueKind.Number
                    ? minEl.GetDouble() : (double?)null;
                var maximum = schema.TryGetProperty("maximum", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number
                    ? maxEl.GetDouble() : (double?)null;

                fields.Add(new SchemaField(key, label, kind, required.Contains(key), minimum, maximum, enumValues));
            }

            return fields;
        }
    }
}

public enum SchemaFieldKind { String, Integer, Number, Boolean, Enum }

public sealed record SchemaField(
    string Key,
    string Label,
    SchemaFieldKind Kind,
    bool Required,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<string>? EnumValues);
