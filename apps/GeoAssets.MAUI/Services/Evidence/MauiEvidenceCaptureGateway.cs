using GeoAssets.Shared.Services.Evidence;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;

namespace GeoAssets.MAUI.Services.Evidence;

/// <summary>
/// The MAUI-side half of XD01-244's capture flow: resolves a GPS fix and compass heading from
/// device hardware, hard-blocks <em>before ever opening the camera</em> when
/// <see cref="GpsAccuracyGate"/> rejects the fix (the ticket's literal "shutter is hard-blocked"
/// requirement — there is no code path here that reaches <see cref="MediaPicker.CapturePhotoAsync"/>
/// on a rejected fix), then hands the captured bytes to the portable, testable
/// <see cref="IEvidenceCaptureService"/> (<c>GeoAssets.Shared</c>) for watermarking and signing.
/// </summary>
internal sealed class MauiEvidenceCaptureGateway(IEvidenceCaptureService captureService, ITrustedTimestampProvider trustedTimestampProvider)
{
    public async Task<EvidenceCaptureResult> CaptureAsync(EvidenceCaptureContext context, string tenantSecret, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fix = await GetGpsFixAsync(ct).ConfigureAwait(false);
        if (!GpsAccuracyGate.IsAcceptable(fix))
        {
            throw new EvidenceCaptureRejectedException(fix.IsFromMockProvider
                ? "Location reports a mock provider; the camera will not open."
                : $"GPS horizontal accuracy {fix.HorizontalAccuracyMeters:F1}m exceeds the {GpsAccuracyGate.MaxHorizontalAccuracyMeters:F0}m limit; the camera will not open.");
        }

        var heading = await TryGetCompassHeadingAsync().ConfigureAwait(false);
        var tilt = await TryGetTiltDegreesAsync().ConfigureAwait(false);
        var trustedTimestamp = await trustedTimestampProvider.GetUtcNowAsync(ct).ConfigureAwait(false);
        var contextWithCaptureMetadata = context with
        {
            CompassHeadingDegrees = heading,
            TiltDegrees = tilt,
            CapturedAtUtc = trustedTimestamp,
        };

        var photo = await MediaPicker.Default.CapturePhotoAsync().ConfigureAwait(false)
            ?? throw new EvidenceCaptureRejectedException("Camera capture was cancelled.");

        using var stream = await photo.OpenReadAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);

        return captureService.Capture(buffer.ToArray(), fix, contextWithCaptureMetadata, tenantSecret);
    }

    private static async Task<GpsFix> GetGpsFixAsync(CancellationToken ct)
    {
        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>().ConfigureAwait(false);
        if (status != PermissionStatus.Granted)
            throw new EvidenceCaptureRejectedException("Location permission was not granted.");

        var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10));
        var location = await Geolocation.Default.GetLocationAsync(request, ct).ConfigureAwait(false)
            ?? throw new EvidenceCaptureRejectedException("Unable to determine device location.");

        // A platform that can't report accuracy fails the gate closed (MaxValue), not open.
        return new GpsFix(location.Latitude, location.Longitude, location.Accuracy ?? double.MaxValue, location.IsFromMockProvider);
    }

    /// <summary>Returns null (no compass hardware or no reading within the timeout) rather than blocking capture — XD01-244 gates on GPS, not compass.</summary>
    private static async Task<double?> TryGetCompassHeadingAsync()
    {
        var readingReceived = new TaskCompletionSource<double?>();
        void OnReadingChanged(object? sender, CompassChangedEventArgs e) => readingReceived.TrySetResult(e.Reading.HeadingMagneticNorth);

        try
        {
            Compass.Default.ReadingChanged += OnReadingChanged;
            Compass.Default.Start(SensorSpeed.UI);

            var completed = await Task.WhenAny(readingReceived.Task, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
            return completed == readingReceived.Task ? await readingReceived.Task.ConfigureAwait(false) : null;
        }
        catch (FeatureNotSupportedException)
        {
            return null;
        }
        finally
        {
            Compass.Default.ReadingChanged -= OnReadingChanged;
            if (Compass.Default.IsMonitoring)
                Compass.Default.Stop();
        }
    }

    /// <summary>
    /// Returns the device's inclination from horizontal (0 = flat, 90 = upright) derived from the
    /// accelerometer's gravity vector, or null (no accelerometer or no reading within the timeout)
    /// rather than blocking capture — XD01-235 gates on GPS, not tilt.
    /// </summary>
    private static async Task<double?> TryGetTiltDegreesAsync()
    {
        var readingReceived = new TaskCompletionSource<double?>();
        void OnReadingChanged(object? sender, AccelerometerChangedEventArgs e)
        {
            var acceleration = e.Reading.Acceleration;
            var magnitude = Math.Sqrt(acceleration.X * acceleration.X + acceleration.Y * acceleration.Y + acceleration.Z * acceleration.Z);
            if (magnitude < 0.1)
            {
                readingReceived.TrySetResult(null);
                return;
            }

            var angleFromVertical = Math.Acos(Math.Clamp(Math.Abs(acceleration.Z) / magnitude, -1.0, 1.0)) * (180.0 / Math.PI);
            readingReceived.TrySetResult(90.0 - angleFromVertical);
        }

        try
        {
            Accelerometer.Default.ReadingChanged += OnReadingChanged;
            Accelerometer.Default.Start(SensorSpeed.UI);

            var completed = await Task.WhenAny(readingReceived.Task, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
            return completed == readingReceived.Task ? await readingReceived.Task.ConfigureAwait(false) : null;
        }
        catch (FeatureNotSupportedException)
        {
            return null;
        }
        finally
        {
            Accelerometer.Default.ReadingChanged -= OnReadingChanged;
            if (Accelerometer.Default.IsMonitoring)
                Accelerometer.Default.Stop();
        }
    }
}
