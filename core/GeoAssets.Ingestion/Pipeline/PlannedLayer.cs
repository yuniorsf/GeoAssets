using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>A layer file set selected for a pass, with its profile definition and its elements' scope.</summary>
internal sealed record PlannedLayer(LayerFileSet FileSet, LayerDefinition Definition, string Scope);
