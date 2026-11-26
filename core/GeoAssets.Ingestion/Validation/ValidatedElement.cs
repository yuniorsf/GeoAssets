using GeoAssets.Ingestion.Model;
using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Validation;

/// <summary>An element that passed the shape rules: its asset class and its (possibly unwrapped) native geometry.</summary>
internal sealed record ValidatedElement(CanonicalElement Element, AssetClassDefinition AssetClass, Geometry NativeGeometry);
