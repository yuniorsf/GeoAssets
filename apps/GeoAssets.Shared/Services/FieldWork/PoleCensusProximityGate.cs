using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services.Evidence;

namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// XD01-260's hard-block interlock rule: pole census capture is rejected unless the current
/// <see cref="GpsFix"/> both passes <see cref="GpsAccuracyGate"/> (accuracy, not a mock
/// provider — the same check <see cref="GpsAccuracyGate"/> already enforces for evidence
/// photos, XD01-244) <em>and</em> is within <see cref="MaxProximityMeters"/> of the pole being
/// inspected. Pure and testable — the caller (a MAUI host checking this before opening the
/// capture form, the literal "camera/form capture is blocked" AC) decides what happens on
/// rejection.
/// </summary>
public static class PoleCensusProximityGate
{
    public const double MaxProximityMeters = 15.0;

    public static bool IsAcceptable(GpsFix fix, GeoPoint poleLocation)
    {
        ArgumentNullException.ThrowIfNull(fix);
        ArgumentNullException.ThrowIfNull(poleLocation);

        if (!GpsAccuracyGate.IsAcceptable(fix))
            return false;

        var fixPoint = new GeoPoint(fix.Longitude, fix.Latitude);
        return GeodeticDistanceCalculator.IsWithinDistance(fixPoint, poleLocation, MaxProximityMeters);
    }
}
