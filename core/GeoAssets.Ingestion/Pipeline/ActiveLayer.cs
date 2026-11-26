using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Spatial;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>A pass-2 layer being imported: its plan, its mapper and the transformer of its CRS.</summary>
internal sealed record ActiveLayer(PlannedLayer Plan, ILayerMapper Mapper, ICoordinateTransformer Transformer);
