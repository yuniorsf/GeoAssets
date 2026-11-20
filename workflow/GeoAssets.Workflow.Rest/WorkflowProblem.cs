using System.Text.Json;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// A parsed <c>application/problem+json</c> body: its stable <see cref="Code"/>, its
/// human-readable <see cref="Detail"/> (falling back to the title), and the whole body for the
/// extension members a particular code carries.
/// </summary>
public sealed record WorkflowProblem(string Code, string Detail, JsonElement Body)
{
    public string? GetString(string member) =>
        Body.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The strings of an array member; empty when the member is absent or not an array of strings.</summary>
    public IReadOnlyList<string> GetStrings(string member) =>
        Body.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    public int GetInt32(string member) =>
        Body.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
}
