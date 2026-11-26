namespace GeoAssets.Ingestion.Model;

/// <summary>A reference resolved to a target element's feature id. <see cref="DistanceMeters"/> is set for snaps.</summary>
public sealed record ResolvedReference(
    ElementReference Reference,
    string TargetFeatureId,
    ResolutionMethod Method,
    double? DistanceMeters);
