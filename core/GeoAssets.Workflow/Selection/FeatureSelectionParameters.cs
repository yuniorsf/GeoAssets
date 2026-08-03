using System.Text.Json;

namespace GeoAssets.Workflow.Selection;

/// <summary>
/// Reads a <see cref="FeatureSelectionSpec.Parameters"/> value regardless of whether it's
/// still the original CLR object (fresh, never persisted) or a <see cref="JsonElement"/>
/// (after a JSON round-trip through <c>FeatureSelectionSpec</c> persistence — see
/// <c>ServiceOrderMapper</c>). <c>System.Text.Json</c> deserializes an <c>object</c>-typed
/// dictionary value into a boxed <see cref="JsonElement"/>, not the original CLR type, so a
/// raw cast or <see cref="Convert"/> call that only handled the fresh case would throw
/// <see cref="InvalidCastException"/> on any reloaded order.
/// </summary>
public static class FeatureSelectionParameters
{
    public static double ToDouble(object value) => value switch
    {
        JsonElement je => je.GetDouble(),
        var v => Convert.ToDouble(v),
    };

    public static bool ToBoolean(object value) => value switch
    {
        JsonElement je => je.GetBoolean(),
        var v => Convert.ToBoolean(v),
    };

    public static string ToStringValue(object value) => value switch
    {
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString()!,
        var v => throw new InvalidCastException($"Value of type '{v?.GetType().Name ?? "null"}' is not a string."),
    };

    public static TEnum ToEnum<TEnum>(object value) where TEnum : struct, Enum => value switch
    {
        TEnum e => e,
        JsonElement { ValueKind: JsonValueKind.String } je => Enum.Parse<TEnum>(je.GetString()!, ignoreCase: true),
        JsonElement { ValueKind: JsonValueKind.Number } je => (TEnum)Enum.ToObject(typeof(TEnum), je.GetInt32()),
        var v => throw new InvalidCastException($"Value of type '{v?.GetType().Name ?? "null"}' is not a {typeof(TEnum).Name}."),
    };

    public static T To<T>(object value) => value switch
    {
        T typed => typed,
        JsonElement je => je.Deserialize<T>() ?? throw new InvalidOperationException(
            $"Value deserialized to null for type {typeof(T).Name}."),
        var v => throw new InvalidCastException($"Value of type '{v?.GetType().Name ?? "null"}' is not a {typeof(T).Name}."),
    };

    public static IReadOnlyList<string> ToStringList(object value) => value switch
    {
        IReadOnlyList<string> list => list,
        IEnumerable<string> seq => [.. seq],
        JsonElement { ValueKind: JsonValueKind.Array } je => [.. je.EnumerateArray().Select(e => e.GetString()!)],
        var v => throw new InvalidCastException($"Value of type '{v?.GetType().Name ?? "null"}' is not a string list."),
    };

    // ── Dictionary convenience wrappers (key lookup + convert) ──────────────────

    public static double GetDouble(this IReadOnlyDictionary<string, object> parameters, string key)
        => ToDouble(parameters[key]);

    public static bool GetBoolean(this IReadOnlyDictionary<string, object> parameters, string key)
        => ToBoolean(parameters[key]);

    public static string GetString(this IReadOnlyDictionary<string, object> parameters, string key)
        => ToStringValue(parameters[key]);

    public static TEnum GetEnum<TEnum>(this IReadOnlyDictionary<string, object> parameters, string key)
        where TEnum : struct, Enum
        => ToEnum<TEnum>(parameters[key]);

    public static T GetValue<T>(this IReadOnlyDictionary<string, object> parameters, string key)
        => To<T>(parameters[key]);

    public static IReadOnlyList<string> GetStringList(this IReadOnlyDictionary<string, object> parameters, string key)
        => ToStringList(parameters[key]);
}
