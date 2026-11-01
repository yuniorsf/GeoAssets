namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// Thrown when a <see cref="GpsFix"/> fails <see cref="GpsAccuracyGate"/> — XD01-244's "shutter
/// is hard-blocked" acceptance criterion. A MAUI host checks <see cref="GpsAccuracyGate.IsAcceptable"/>
/// itself before ever invoking the camera (the actual hard block — no shutter UI is shown at
/// all); <see cref="IEvidenceCaptureService.Capture"/> re-checks and throws this as a second,
/// server-side-equivalent line of defense for a caller that skips the pre-check.
/// </summary>
public sealed class EvidenceCaptureRejectedException : Exception
{
    public EvidenceCaptureRejectedException() { }
    public EvidenceCaptureRejectedException(string message) : base(message) { }
    public EvidenceCaptureRejectedException(string message, Exception innerException) : base(message, innerException) { }
}
