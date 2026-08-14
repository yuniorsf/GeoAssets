using System.Text.Json;
using System.Text.Json.Nodes;
using GeoAssets.Core.Models;
using Json.Schema;

namespace GeoAssets.Core.Services;

/// <summary>
/// Validates a <see cref="GeoFeatureProperties.CustomAttributes"/> dictionary against the JSON
/// Schema (draft 2020-12) declared on its <see cref="AssetType.AttributesSchemaJson"/>, if any.
///
/// Generalizes <c>GeoAssets.Workflow.Orders.ServiceOrderAttributeValidator</c> (XD01-2) to
/// <see cref="AssetType"/>: same JSON-or-string-fallback coercion so schema authors can use real
/// <c>"integer"</c>/<c>"boolean"</c>/<c>"number"</c> constraints against the string dictionary values.
/// </summary>
public static class GeoFeatureAttributeValidator
{
    /// <summary>
    /// Returns validation errors (empty when valid). An <see cref="AssetType"/> with no
    /// <see cref="AssetType.AttributesSchemaJson"/> is unrestricted — always valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(AssetType assetType, IReadOnlyDictionary<string, string> attributes)
    {
        if (string.IsNullOrWhiteSpace(assetType.AttributesSchemaJson))
            return [];

        var schema = JsonSchema.FromText(assetType.AttributesSchemaJson);

        var instance = new JsonObject();
        foreach (var (key, value) in attributes)
            instance[key] = ParseAsJsonOrString(value);

        var element = instance.Deserialize<JsonElement>();
        var result = schema.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });

        return result.IsValid ? [] : [.. CollectErrors(result)];
    }

    /// <summary>Throws <see cref="GeoFeatureAttributeValidationException"/> when invalid.</summary>
    public static void EnsureValid(AssetType assetType, IReadOnlyDictionary<string, string> attributes)
    {
        var errors = Validate(assetType, attributes);
        if (errors.Count > 0)
            throw new GeoFeatureAttributeValidationException(assetType.Id, errors);
    }

    private static IEnumerable<string> CollectErrors(EvaluationResults result)
    {
        if (result.Errors is { Count: > 0 })
            foreach (var (keyword, message) in result.Errors)
                yield return $"{result.InstanceLocation}: [{keyword}] {message}";

        foreach (var detail in result.Details ?? [])
        {
            if (detail.IsValid) continue;
            foreach (var error in CollectErrors(detail))
                yield return error;
        }
    }

    private static JsonNode? ParseAsJsonOrString(string value)
    {
        try { return JsonNode.Parse(value); }
        catch (JsonException) { return JsonValue.Create(value); }
    }
}
