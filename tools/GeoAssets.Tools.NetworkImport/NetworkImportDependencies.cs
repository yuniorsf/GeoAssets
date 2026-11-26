namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// What a run talks to outside the process: the HTTP handler every kernel <see cref="HttpClient"/> is built on, the
/// clock (retry back-off, default import id) and the environment (bearer token / client credentials).
/// </summary>
internal sealed record NetworkImportDependencies(
    HttpMessageHandler Handler,
    TimeProvider TimeProvider,
    Func<string, string?> GetEnvironmentVariable);
