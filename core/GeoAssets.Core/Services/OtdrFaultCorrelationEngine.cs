using System.Globalization;
using GeoAssets.Core.Models.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

namespace GeoAssets.Core.Services;

/// <summary>
/// XD01-282's own ticket description names "the doc's specified algorithm" for this engine, but no
/// such document exists in this ticket's Jira attachments, remote links, or the project's
/// Confluence space (all three were searched before writing this) — so the algorithm and public
/// signatures below are this implementation's own design, derived directly from the ticket's AC
/// prose and standard outside-plant OTDR fiber-engineering practice (sheath-length conversion via
/// helix factor, central-office slack boundary, sequential slack/splice-coil traversal, UTM linear
/// referencing). Flagged explicitly in this ticket's closing comment for a reviewer to confirm
/// against whatever source "the doc" actually refers to, if one exists outside this repo's reach.
///
/// Algorithm: OTDR reports <see cref="OtdrFaultCorrelationRequest.OpticalDistanceMeters"/>, the
/// distance light traveled along the fiber core. Dividing by
/// <see cref="OtdrFaultCorrelationRequest.HelixFactor"/> converts that to physical sheath length
/// (the fiber lies in a slight helix inside the cable jacket, so it is longer than the jacket's
/// own run). If sheath length doesn't exceed the central-office slack, the fault is still inside
/// the CO building (<see cref="FaultLocationType.StationSlack"/>, distance 0, no coordinate).
/// Otherwise the remaining length is walked across the route's ordered slack/splice points: each
/// aerial span between two points maps 1:1 to route distance, while each point itself consumes a
/// fixed coiled length that does not. The route distance the fault resolves to is then indexed
/// into the cable's UTM-reprojected geometry via NTS <see cref="LengthIndexedLine"/>, and that
/// point reprojected back to WGS84 — the round trip XD01-282's AC3 asks for. Finally the ordered
/// poles along the route are scanned for the bounding upstream/downstream pair.
/// </summary>
public sealed class OtdrFaultCorrelationEngine : IOtdrFaultCorrelationEngine
{
    public OtdrFaultCorrelationResult Correlate(OtdrFaultCorrelationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CableRoute);
        ArgumentNullException.ThrowIfNull(request.SlackPoints);
        ArgumentNullException.ThrowIfNull(request.PolesAlongRoute);
        ArgumentOutOfRangeException.ThrowIfNegative(request.OpticalDistanceMeters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.HelixFactor);
        ArgumentOutOfRangeException.ThrowIfNegative(request.CentralOfficeSlackMeters);

        var sheathLength = request.OpticalDistanceMeters / request.HelixFactor;
        if (sheathLength <= request.CentralOfficeSlackMeters)
        {
            return new OtdrFaultCorrelationResult(
                FaultLocationType.StationSlack, 0.0, null, null, null,
                string.Create(CultureInfo.InvariantCulture, $"Fault resolves within central office slack (sheath length {sheathLength:F1}m <= CO slack {request.CentralOfficeSlackMeters:F1}m); no outside-plant coordinate available."));
        }

        var (locationType, routeDistance, summary) = WalkSlackPoints(request, sheathLength - request.CentralOfficeSlackMeters);
        var coordinate = ExtractCoordinate(request.CableRoute, routeDistance);
        var (upstream, downstream) = CorrelatePoles(request.PolesAlongRoute, routeDistance);
        return new OtdrFaultCorrelationResult(locationType, routeDistance, coordinate, upstream, downstream, summary);
    }

    private static (FaultLocationType LocationType, double RouteDistanceMeters, string Summary) WalkSlackPoints(
        OtdrFaultCorrelationRequest request, double remaining)
    {
        var consumed = 0.0;
        foreach (var point in request.SlackPoints.OrderBy(p => p.RouteDistanceMeters))
        {
            var span = point.RouteDistanceMeters - consumed;
            if (remaining <= span)
            {
                var routeDistance = consumed + remaining;
                return (FaultLocationType.AerialSpan, routeDistance, string.Create(CultureInfo.InvariantCulture,
                    $"Fault resolves to an aerial span, {remaining:F1}m past route position {consumed:F1}m."));
            }

            remaining -= span;
            consumed = point.RouteDistanceMeters;

            if (remaining <= request.SpliceMatchToleranceMeters)
            {
                return (point.Kind, consumed, string.Create(CultureInfo.InvariantCulture,
                    $"Fault resolves to the {point.Kind} at route position {consumed:F1}m (within {request.SpliceMatchToleranceMeters:F1}m tolerance)."));
            }

            remaining = Math.Max(0, remaining - point.CoiledLengthMeters);
        }

        var finalRouteDistance = consumed + remaining;
        return (FaultLocationType.AerialSpan, finalRouteDistance, string.Create(CultureInfo.InvariantCulture,
            $"Fault resolves to an aerial span, {remaining:F1}m beyond the last known slack/splice point at route position {consumed:F1}m."));
    }

    private static GeoPoint ExtractCoordinate(GeoLineString cableRoute, double routeDistanceMeters)
    {
        var wgs84Coordinates = cableRoute.LineString.Coordinates;
        if (wgs84Coordinates.Length < 2)
            throw new ArgumentException("Cable route must have at least 2 coordinates.", nameof(cableRoute));

        var zone = SirgasUtmZoneResolver.ZoneFor(wgs84Coordinates[0].X);
        var epsgCode = SirgasUtmZoneResolver.EpsgCodeFor(wgs84Coordinates[0].X);

        var utmCoordinates = wgs84Coordinates
            .Select(c => SirgasUtmReprojector.ToSirgasUtm(new GeoPoint(c.X, c.Y)))
            .Select(p => new Coordinate(p.Easting, p.Northing))
            .ToArray();
        var utmLineString = GeoFactory.For(epsgCode).CreateLineString(utmCoordinates);

        var utmPoint = new LengthIndexedLine(utmLineString).ExtractPoint(routeDistanceMeters);
        return SirgasUtmReprojector.FromSirgasUtm(utmPoint.X, utmPoint.Y, zone);
    }

    private static (string? Upstream, string? Downstream) CorrelatePoles(
        IReadOnlyList<OtdrPoleAlongRoute> polesAlongRoute, double routeDistanceMeters)
    {
        string? upstream = null;
        string? downstream = null;
        foreach (var pole in polesAlongRoute.OrderBy(p => p.RouteDistanceMeters))
        {
            if (pole.RouteDistanceMeters <= routeDistanceMeters)
            {
                upstream = pole.PoleFeatureId;
            }
            else
            {
                downstream = pole.PoleFeatureId;
                break;
            }
        }

        return (upstream, downstream);
    }
}
