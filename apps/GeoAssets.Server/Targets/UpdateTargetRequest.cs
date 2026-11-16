namespace GeoAssets.Server.Targets;

/// <summary>
/// Body of <c>PATCH …/targets/{targetId}</c>. <see cref="Version"/> is required and must be the
/// version the caller read. <see cref="ActivityIds"/> and <see cref="Status"/> are left
/// unchanged when null or absent; <c>note</c> is left unchanged when absent and cleared when
/// present and null.
/// </summary>
public sealed record UpdateTargetRequest(IReadOnlyList<string>? ActivityIds, string? Note, string? Status, int Version);
