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
        ArgumentNullException.ThrowIfNull(orderType);
        ArgumentNullException.ThrowIfNull(attributes);

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
        ArgumentNullException.ThrowIfNull(orderType);

        var errors = Validate(orderType, attributes);
        if (errors.Count > 0)
            throw new ServiceOrderAttributeValidationException(orderType.Id, errors);
    }

    /// <summary>
    /// Validates a candidate <see cref="OrderType.AttributesSchemaJson"/> document itself —
    /// before it is persisted — rather than validating order attribute values against an
    /// already-trusted schema (that is what <see cref="Validate"/> does). Rejects anything
    /// that isn't well-formed JSON, anything <see cref="JsonSchema.FromText(string)"/> can't
    /// parse as a schema, a <c>$schema</c> keyword naming a dialect other than draft 2020-12,
    /// and anything the draft 2020-12 meta-schema itself rejects — so a malformed dialect form
    /// (e.g. a client admin's own JSON Schema for <c>OrderType.AttributesSchemaJson</c>) is
    /// caught here instead of silently corrupting live order intake later.
    /// </summary>
    public static IReadOnlyList<string> ValidateSchemaDocument(string schemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);

        JsonElement element;
        try
        {
            element = JsonDocument.Parse(schemaJson).RootElement;
        }
        catch (JsonException ex)
        {
            return [$"Not valid JSON: {ex.Message}"];
        }

        if (element.TryGetProperty("$schema", out var declaredDialect))
        {
            var isDraft202012 = declaredDialect.ValueKind == JsonValueKind.String
                && Uri.TryCreate(declaredDialect.GetString(), UriKind.Absolute, out var declaredUri)
                && declaredUri == MetaSchemas.Draft202012Id;

            if (!isDraft202012)
                return [$"'$schema' must be \"{MetaSchemas.Draft202012Id}\" (draft 2020-12) when present; found {declaredDialect.GetRawText()}"];
        }

        try
        {
            JsonSchema.FromText(schemaJson);
        }
        catch (JsonSchemaException ex)
        {
            return [ex.Message];
        }

        var metaResult = MetaSchemas.Draft202012.Evaluate(element, new EvaluationOptions { OutputFormat = OutputFormat.List });
        return metaResult.IsValid ? [] : [.. CollectErrors(metaResult)];
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
