using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Validates a <see cref="ServiceOrder.Attributes"/> dictionary against the JSON Schema
/// (draft 2020-12) declared on its <see cref="OrderType.AttributesSchemaJson"/>, if any.
///
/// <see cref="ServiceOrder.Attributes"/> is a plain <c>Dictionary&lt;string,string&gt;</c> —
/// every value is a string. To let schema authors use real <c>"integer"</c>/<c>"boolean"</c>/
/// <c>"number"</c> constraints (not just <c>"string"</c> + regex), each value is first tried
/// as JSON on its own (so an attribute stored as the text <c>"5"</c> validates against
/// <c>{"type": "integer"}</c>); a value that isn't valid JSON by itself (e.g. free text like
/// <c>"Downtown Depot"</c>) falls back to being validated as a JSON string.
/// </summary>
public static class ServiceOrderAttributeValidator
{
    /// <summary>
    /// Returns validation errors (empty when valid). An <see cref="OrderType"/> with no
    /// <see cref="OrderType.AttributesSchemaJson"/> is unrestricted — always valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(OrderType orderType, IReadOnlyDictionary<string, string> attributes)
    {
        if (string.IsNullOrWhiteSpace(orderType.AttributesSchemaJson))
            return [];

        var schema = JsonSchema.FromText(orderType.AttributesSchemaJson);

        var instance = new JsonObject();
        foreach (var (key, value) in attributes)
            instance[key] = ParseAsJsonOrString(value);

        var element = instance.Deserialize<JsonElement>();
        var result = schema.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });

        return result.IsValid ? [] : [.. CollectErrors(result)];
    }

    /// <summary>Throws <see cref="ServiceOrderAttributeValidationException"/> when invalid.</summary>
    public static void EnsureValid(OrderType orderType, IReadOnlyDictionary<string, string> attributes)
    {
        var errors = Validate(orderType, attributes);
        if (errors.Count > 0)
            throw new ServiceOrderAttributeValidationException(orderType.Id, errors);
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
