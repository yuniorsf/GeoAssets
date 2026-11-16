using System.Text.Json;
using System.Text.Json.Serialization;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Maps <see cref="OrderTargetPolicy"/> to and from the JSON text stored in
/// <c>OrderTypes.TargetPolicyJson</c>. Enums are written by name, so the stored document
/// survives a reordering of the enum members.
/// </summary>
internal static class OrderTargetPolicyMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Null policy maps to a null column.</summary>
    public static string? ToJson(OrderTargetPolicy? policy)
        => policy is null ? null : JsonSerializer.Serialize(policy, JsonOptions);

    /// <summary>A null or blank column maps to "no policy".</summary>
    public static OrderTargetPolicy? FromJson(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<OrderTargetPolicy>(json, JsonOptions);
}
