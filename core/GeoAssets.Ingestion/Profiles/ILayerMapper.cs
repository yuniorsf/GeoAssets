using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles;

/// <summary>
/// Maps a profile layer's raw records to canonical elements. Must handle <see cref="LayerVerdict.GeometryOnly"/>
/// records (empty attributes): coordinate key, <see cref="Model.Completeness.GeometryOnly"/>, and snap references
/// where the geometry allows (<see cref="Model.Completeness.Recovered"/> once resolved).
/// </summary>
public interface ILayerMapper
{
    MappingResult Map(RawRecord record, string scope);
}
