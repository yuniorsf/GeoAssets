namespace GeoAssets.Server.Tests.Routing;

/// <summary>Lines added to / removed from a route inventory relative to its snapshot.</summary>
internal sealed record RouteInventoryDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    public string Format() =>
        string.Join(
            Environment.NewLine,
            Removed.Select(line => $"- {line}").Concat(Added.Select(line => $"+ {line}")));
}
