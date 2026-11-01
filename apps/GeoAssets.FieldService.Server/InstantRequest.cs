namespace GeoAssets.FieldService.Server;

/// <summary>A single instant in time, provided by the caller rather than read from a server clock — used by the issue-notice/resolve-notice endpoints.</summary>
internal sealed record InstantRequest(DateTimeOffset AsOf);
