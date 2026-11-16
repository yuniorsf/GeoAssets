using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Services.Evidence;
using GeoAssets.Shared.Services.FieldWork;
using Microsoft.Maui.Devices.Sensors;

namespace GeoAssets.MAUI.Services.FieldWork;

/// <summary>
/// The MAUI-side half of XD01-260's pole census flow: looks the pole up via
/// <see cref="IGeoCoreClient"/> and resolves a GPS fix, hard-blocking before ever showing the
/// capture form when <see cref="PoleCensusProximityGate"/> rejects it (the ticket's literal
/// "camera/form capture is blocked when GPS accuracy or proximity fails the interlock"
/// requirement — there is no code path here that returns an acceptable result on a rejected
/// fix), the same "block before ever reaching the UI" shape
/// <see cref="GeoAssets.MAUI.Services.Evidence.MauiEvidenceCaptureGateway"/> already established
/// for evidence photos (XD01-244).
/// </summary>
internal sealed class MauiPoleCensusGateway(IGeoCoreClient geoCoreClient)
{
    public Task<GeoFeature?> LookupPoleAsync(string poleId, CancellationToken ct = default) =>
        geoCoreClient.GetFeatureByIdAsync(poleId, ct);

    /// <summary>
    /// Resolves the device's current GPS fix and checks it against <paramref name="pole"/>'s
    /// location. Returns <c>null</c> when the interlock rejects the fix (closed camera/form),
    /// never a fix the caller still has to re-check.
    /// </summary>
    public static async Task<GpsFix?> TryGetInterlockedFixAsync(GeoFeature pole, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pole);

        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>().ConfigureAwait(false);
        if (status != PermissionStatus.Granted)
            return null;

        var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10));
        var location = await Geolocation.Default.GetLocationAsync(request, ct).ConfigureAwait(false);
        if (location is null)
            return null;

        // A platform that can't report accuracy fails the gate closed (MaxValue), not open —
        // same convention MauiEvidenceCaptureGateway's own GpsFix construction uses.
        var fix = new GpsFix(location.Latitude, location.Longitude, location.Accuracy ?? double.MaxValue, location.IsFromMockProvider);

        if (pole.Geometry is null)
            return null;

        return PoleCensusProximityGate.IsAcceptable(fix, pole.Geometry.Centroid) ? fix : null;
    }
}
