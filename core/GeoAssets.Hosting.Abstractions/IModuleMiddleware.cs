using Microsoft.AspNetCore.Builder;

namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// Optional request-pipeline step a <see cref="IGeoAssetsModule"/> can also implement (XD01-359): middleware the host places
/// where it calls <see cref="GeoAssetsModuleHostingExtensions.UseGeoAssetsModules"/> — after authentication, so the
/// middleware sees the caller — in the enabled order.
/// </summary>
public interface IModuleMiddleware
{
    void UseMiddleware(IApplicationBuilder app);
}
