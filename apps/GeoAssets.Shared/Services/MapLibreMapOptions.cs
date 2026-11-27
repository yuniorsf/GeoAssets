namespace GeoAssets.Shared.Services;

/// <summary>
/// What <c>GeoAssetsMapLibre.initializeMap</c> receives besides the view (XD01-435), camelCase over JS interop.
/// </summary>
/// <param name="StyleUrl">The kernel's tile style (<see cref="MapLibreOptions.ResolveStyleUrl"/>); the bearer token goes
/// to the requests under its directory only.</param>
/// <param name="GatewayBaseUrl">The gateway's base URL (<c>GeoAssetsServer:BaseUrl</c>), when known — a WMS layer under it
/// is the kernel's EPSG:4326-only WMS, which the MapLibre mode does not draw.</param>
/// <param name="LibraryBaseUrl">Where the libraries are loaded from (<see cref="MapLibreOptions.LibraryBaseUrl"/>, XD01-460);
/// <c>null</c> = the pinned CDN.</param>
/// <param name="LongPressContextMenu">Whether a long press opens a feature's context menu
/// (<see cref="MapLibreOptions.LongPressContextMenu"/>, XD01-460).</param>
public sealed record MapLibreMapOptions(string StyleUrl, string? GatewayBaseUrl, string? LibraryBaseUrl = null, bool LongPressContextMenu = false);
