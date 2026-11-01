namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// XD01-244's shutter interlock rule: a <see cref="GpsFix"/> is acceptable only within
/// <see cref="MaxHorizontalAccuracyMeters"/> and never from a mock location provider. Pure and
/// testable — the caller (<see cref="IEvidenceCaptureService"/>, or a MAUI host checking this
/// before ever opening the camera for a true hard block) decides what "unacceptable" means for
/// the capture flow.
/// </summary>
public static class GpsAccuracyGate
{
    public const double MaxHorizontalAccuracyMeters = 10.0;

    public static bool IsAcceptable(GpsFix fix)
    {
        ArgumentNullException.ThrowIfNull(fix);
        return !fix.IsFromMockProvider && fix.HorizontalAccuracyMeters <= MaxHorizontalAccuracyMeters;
    }
}
