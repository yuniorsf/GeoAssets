namespace GeoAssets.Shared.Services.Evidence;

/// <summary>
/// A single GNSS reading at photo-capture time (XD01-244). <see cref="IsFromMockProvider"/>
/// mirrors Android's <c>Location.isFromMockProvider()</c> via MAUI Essentials'
/// <c>Microsoft.Maui.Devices.Sensors.Location.IsFromMockProvider</c> — reliable on Android only;
/// other platforms have no equivalent OS concept and always report <c>false</c> here, so
/// <see cref="GpsAccuracyGate"/>'s mock-provider check is a no-op gate on those platforms.
/// </summary>
public sealed record GpsFix(double Latitude, double Longitude, double HorizontalAccuracyMeters, bool IsFromMockProvider);
