using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Resolution;
using GeoAssets.Ingestion.Validation;
using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Projection;

/// <summary>Everything the projector needs for one element: reprojected geometry, completeness, accuracy and edges.</summary>
internal sealed record ProjectionInput(
    ValidatedElement Element,
    FeatureIdentity Id,
    string Scope,
    Geometry GeographicGeometry,
    Completeness Completeness,
    string PositionAccuracy,
    IReadOnlyList<TopoEdge> Edges);
