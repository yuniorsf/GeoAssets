using System.Globalization;
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
        var v => Convert.ToDouble(v, CultureInfo.InvariantCulture),
    };

    public static bool ToBoolean(object value) => value switch
    {
        JsonElement je => je.GetBoolean(),
        var v => Convert.ToBoolean(v, CultureInfo.InvariantCulture),
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
        JsonElement { ValueKind: JsonValueKind.Array } je => [.. je.EnumerateArray().Select(StringElement)],
        var v => throw new InvalidCastException($"Value of type '{v?.GetType().Name ?? "null"}' is not a string list."),
    };

    private static string StringElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String or JsonValueKind.Null => element.GetString()!,
        var kind => throw new InvalidCastException($"A JSON {kind} element is not a string."),
    };

    public static int ToInt32(object value) => value switch
    {
        JsonElement je => je.GetInt32(),
        var v => Convert.ToInt32(v, CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Reads RFC 7946 polygon coordinates (rings of [longitude, latitude] positions). Only the
    /// nesting is checked here — a missing ring or a malformed position is carried through as-is
    /// for the fence validator to report.
    /// </summary>
    public static double[][][] ToRings(object value) => value switch
    {
        double[][][] rings => rings,
        IEnumerable<IEnumerable<IEnumerable<double>?>?> nested =>
            [.. nested.Select(ring => ring?.Select(position => position?.ToArray()!).ToArray()!)],
        JsonElement { ValueKind: JsonValueKind.Array } je => RingsFromJson(je),
        var v => throw new InvalidCastException($"Value of type '{v?.GetType().Name ?? "null"}' is not a list of coordinate rings."),
    };

    private static double[][][] RingsFromJson(JsonElement element)
    {
        try
        {
            return element.Deserialize<double[][][]>()!;
        }
        catch (JsonException ex)
        {
            throw new InvalidCastException("The JSON value is not a list of coordinate rings.", ex);
        }
    }

    // ── Dictionary convenience wrappers (key lookup + convert) ──────────────────

    public static double GetDouble(this IReadOnlyDictionary<string, object> parameters, string key)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return ToDouble(parameters[key]);
    }

    public static bool GetBoolean(this IReadOnlyDictionary<string, object> parameters, string key)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return ToBoolean(parameters[key]);
    }

    public static string GetString(this IReadOnlyDictionary<string, object> parameters, string key)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return ToStringValue(parameters[key]);
    }

    public static TEnum GetEnum<TEnum>(this IReadOnlyDictionary<string, object> parameters, string key)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return ToEnum<TEnum>(parameters[key]);
    }

    public static T GetValue<T>(this IReadOnlyDictionary<string, object> parameters, string key)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return To<T>(parameters[key]);
    }

    public static IReadOnlyList<string> GetStringList(this IReadOnlyDictionary<string, object> parameters, string key)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return ToStringList(parameters[key]);
    }

    public static double[][][] GetRings(this IReadOnlyDictionary<string, object> parameters, string key)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return ToRings(parameters[key]);
    }

    // ── Optional parameters (absent key, CLR null and JSON null all read as "not supplied") ──

    public static IReadOnlyList<string>? GetOptionalStringList(this IReadOnlyDictionary<string, object> parameters, string key)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return IsSupplied(parameters, key, out var value) ? ToStringList(value) : null;
    }

    public static int? GetOptionalInt32(this IReadOnlyDictionary<string, object> parameters, string key)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return IsSupplied(parameters, key, out var value) ? ToInt32(value) : null;
    }

    private static bool IsSupplied(IReadOnlyDictionary<string, object> parameters, string key, out object value) =>
        parameters.TryGetValue(key, out value!)
        && value is not null and not JsonElement { ValueKind: JsonValueKind.Null };
}
