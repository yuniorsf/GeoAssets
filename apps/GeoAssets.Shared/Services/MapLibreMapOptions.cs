namespace GeoAssets.Shared.Services;

/// <summary>
/// What <c>GeoAssetsMapLibre.initializeMap</c> receives besides the view (XD01-435), camelCase over JS interop.
/// </summary>
/// <param name="StyleUrl">The kernel's tile style (<see cref="MapLibreOptions.ResolveStyleUrl"/>); the bearer token goes
/// to the requests under its directory only.</param>
/// <param name="GatewayBaseUrl">The gateway's base URL (<c>GeoAssetsServer:BaseUrl</c>), when known — a WMS layer under it
/// is the kernel's EPSG:4326-only WMS, which the MapLibre mode does not draw.</param>
public sealed record MapLibreMapOptions(string StyleUrl, string? GatewayBaseUrl);
