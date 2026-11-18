namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// Where an exception being mapped to a problem answer came from (XD01-363): the module route <see cref="Scope"/> that
/// raised it — a mapper answers only for its own — and the order and target the request was about.
/// </summary>
public sealed record ProblemContext(string Scope, string? OrderId = null, string? TargetId = null);
