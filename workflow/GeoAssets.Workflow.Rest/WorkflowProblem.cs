using System.Text.Json;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// A parsed <c>application/problem+json</c> body: its stable <see cref="Code"/>, its
/// human-readable <see cref="Detail"/> (falling back to the title), and the whole body for the
/// extension members a particular code carries.
/// </summary>
internal sealed record WorkflowProblem(string Code, string Detail, JsonElement Body)
{
    public string? String(string member) =>
        Body.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public int Int32(string member) =>
        Body.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
}
