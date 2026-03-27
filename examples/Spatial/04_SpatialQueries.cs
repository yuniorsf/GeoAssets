// ────────────────────────────────────────────────────────────────────────────
// 04 · Spatial Queries  (NTS-backed)
//
// Demonstrates the geospatial operations exposed by GeoGeometry via
// NetTopologySuite (NTS): predicates, measurements, derived geometries,
// and the repository-level spatial query methods.
//
// Note: all coordinates are in WGS-84 (longitude, latitude).
//       Distance and area are expressed in DEGREES (coordinate units).
//       For metric results, project to a local CRS before measuring.
// ────────────────────────────────────────────────────────────────────────────
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Examples.Spatial;

public static class SpatialQueries
{
    public static void Run()
    {
        // ── 1. Build a set of heterogeneous geographic features ──────────────────

        var parkBoundary = PolygonFeature("Parque Central",
            [[-69.92, 18.46], [-69.90, 18.46], [-69.90, 18.48],
             [-69.92, 18.48], [-69.92, 18.46]]);

        var mainRoad = LineFeature("Av. Principal",
            [[-69.93, 18.47], [-69.89, 18.47], [-69.85, 18.47]]);

        var hospital  = PointFeature("Hospital Central",  (-69.905, 18.47));
        var school    = PointFeature("Escuela del Norte", (-69.91,  18.49));  // outside park
        var monument  = PointFeature("Monumento",         (-69.91,  18.47)); // inside park
        var station   = PointFeature("Estación Metro",    (-69.87,  18.47)); // far from park

        var repo = new InMemoryAssetProvider();
        foreach (var f in new[] { parkBoundary, mainRoad, hospital, school, monument, station })
            repo.Add(f);

        // ── 2. GetWithin — features completely inside the park polygon ────────────

        Print.Section("GetWithin (parque) → activos completamente dentro del parque");
        Print.List("Dentro del parque", repo.GetWithin(parkBoundary.Geometry!));
        // → Monumento (hospital is ON the boundary, so not strictly Within)

        // ── 3. GetIntersecting — features that touch or cross the park ────────────

        Print.Section("GetIntersecting (parque) → activos que tocan o cruzan el parque");
        Print.List("Intersecan el parque", repo.GetIntersecting(parkBoundary.Geometry!));
        // → Park boundary itself, Av. Principal (crosses it), Hospital, Monumento

        // ── 4. GetNearby — features within ~2 km of a reference point ────────────
        //   2 km ≈ 0.018° at lat 18° (rough, for demo purposes)

        Print.Section("GetNearby (Hospital, radio ≈ 2 km) → activos cercanos");
        var hospitalPt = (GeoPoint)hospital.Geometry!;
        Print.List("Cercanos al hospital", repo.GetNearby(hospitalPt, distanceDegrees: 0.025));

        // ── 5. Per-geometry operations on the park polygon ────────────────────────

        Print.Section("Operaciones sobre la geometría del parque (NTS)");
        var park = parkBoundary.Geometry!;

        Console.WriteLine($"      IsValid : {park.IsValid}");
        Console.WriteLine($"      Area    : {park.Area:F6}  (grados²)");
        Console.WriteLine($"      Length  : {park.Length:F6} (grados)");

        var centroid = park.Centroid;
        Console.WriteLine($"      Centroid: [{centroid.Longitude:F5}, {centroid.Latitude:F5}]");

        // ── 6. Buffer — expand the park boundary by ~500 m (≈ 0.0045°) ───────────

        Print.Section("Buffer (parque, +500 m) → zona de influencia");
        var buffer = park.Buffer(0.0045);
        Console.WriteLine($"      Buffer area : {buffer.Area:F6}  (grados²)");
        Console.WriteLine($"      Buffer type : {buffer.GetType().Name}");
        // Buffering a polygon returns a larger Polygon

        // ── 7. Spatial predicates: does the road cross the park? ─────────────────

        Print.Section("Predicados espaciales: ¿la avenida cruza el parque?");
        var road = mainRoad.Geometry!;
        Console.WriteLine($"      Road.Intersects(park)  : {road.Intersects(park)}");
        Console.WriteLine($"      Road.Within(park)      : {road.Within(park)}");
        Console.WriteLine($"      School.Within(park)    : {school.Geometry!.Within(park)}");
        Console.WriteLine($"      Monument.Within(park)  : {monument.Geometry!.Within(park)}");

        // ── 8. Distance between two points ───────────────────────────────────────

        Print.Section("Distance (hospital → estación metro) en grados");
        var dist = hospital.Geometry!.Distance(station.Geometry!);
        Console.WriteLine($"      Distancia: {dist:F5}° ≈ {dist * 111_000:F0} m (aproximado en lat 18°)");

        // ── 9. Bounding box of the road ───────────────────────────────────────────

        Print.Section("GetBoundingBox (Av. Principal) → [minLon, minLat, maxLon, maxLat]");
        var bbox = mainRoad.Geometry!.GetBoundingBox();
        Console.WriteLine($"      [{bbox[0]}, {bbox[1]}, {bbox[2]}, {bbox[3]}]");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeoFeature PolygonFeature(string name, double[][] ring) =>
        new()
        {
            Properties = new() { Name = name, AssetTypeId = AssetType.Area.Id.ToString() },
            Geometry   = new GeoPolygon { Coordinates = [ring] }
        };

    private static GeoFeature LineFeature(string name, double[][] coords) =>
        new()
        {
            Properties = new() { Name = name, AssetTypeId = AssetType.Line.Id.ToString() },
            Geometry   = new GeoLineString { Coordinates = coords }
        };

    private static GeoFeature PointFeature(string name, (double lon, double lat) point) =>
        new()
        {
            Properties = new() { Name = name, AssetTypeId = AssetType.Point.Id.ToString() },
            Geometry   = new GeoPoint { Coordinates = [point.lon, point.lat] }
        };
}
