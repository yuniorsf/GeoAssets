namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Directed topology edge snapshot carried inside asset mutation integration events.
/// </summary>
public sealed record TopoEdgeSnapshot(
    string TargetId,
    string Kind,
    double Weight = 1.0,
    IReadOnlyDictionary<string, string>? Metadata = null);
