namespace GeoAssets.Workflow.Registry;

/// <summary>
/// One action a vertical offers on map features (XD01-369, ADR-001 Part 1.1 "feature actions as data"): on a feature whose
/// asset type carries <paramref name="Role"/>, the verb <paramref name="Verb"/> (shown as <paramref name="LabelKey"/>)
/// creates an order of <paramref name="OrderTypeId"/> through the vertical's <paramref name="Route"/>.
/// </summary>
public sealed record FeatureActionDescriptor(string Role, string Verb, string LabelKey, string OrderTypeId, string Route);
