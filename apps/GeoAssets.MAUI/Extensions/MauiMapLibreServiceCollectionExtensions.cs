using GeoAssets.MAUI.Services.Map;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.MAUI.Extensions;

/// <summary>
/// MAUI's <see cref="MapRenderMode.MapLibre"/> (XD01-460), on top of the map registrations MAUI makes in every mode.
/// With the default render mode (Leaflet) it registers only <see cref="MauiHostPage.Unchanged"/>: the host page, the
/// options and every renderer stay exactly as they were. With <c>MapInterop:RenderMode = MapLibre</c>:
/// <list type="bullet">
///   <item>the host page's Content-Security-Policy is widened for the tile style's origin and MapLibre's worker
///   (<see cref="MapLibreHostPage"/>);</item>
///   <item>MapLibre and terra-draw load from the copies bundled under <c>wwwroot/</c><see cref="LibraryBaseUrl"/>
///   (<see cref="MapLibreOptions.LibraryBaseUrl"/>), unless configured otherwise, and a long press opens a feature's
///   context menu (<see cref="MapLibreOptions.LongPressContextMenu"/>) unless configured off;</item>
///   <item><see cref="MapInteropService"/> is also registered as <see cref="IMapTileLayerInterop"/> — the kernel's entry is
///   drawn from its tiles (<c>HybridLodMapLayerRenderer</c>) — and as <see cref="IMapLocalVectorTileLayerInterop"/> — an
///   offline vector pack is drawn from its tiles (<c>LocalVectorTileMapLayerRenderer</c>).</item>
/// </list>
/// Expects <see cref="MapInteropService"/> registered (scoped) and <c>MapInterop</c> bound to <see cref="MapInteropOptions"/>.
/// </summary>
internal static class MauiMapLibreServiceCollectionExtensions
{
    /// <summary>Where MAUI bundles MapLibre GL JS, terra-draw and its adapter, under its <c>wwwroot</c>.</summary>
    public const string LibraryBaseUrl = "lib/maplibre/";

    public static IServiceCollection AddMauiMapLibreMode(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("MapInterop");
        var options = section.Get<MapInteropOptions>() ?? new MapInteropOptions();
        if (options.RenderMode != MapRenderMode.MapLibre)
        {
            services.AddSingleton(MauiHostPage.Unchanged);
            return services;
        }

        var gateway = configuration["GeoAssetsServer:BaseUrl"] is { Length: > 0 } baseUrl ? new Uri(baseUrl, UriKind.Absolute) : null;
        var styleUrl = new Uri(MapLibreOptions.ResolveStyleUrl(options.MapLibre?.StyleUrl, gateway), UriKind.RelativeOrAbsolute);
        if (!styleUrl.IsAbsoluteUri)
            throw new InvalidOperationException("MapInterop:RenderMode = MapLibre needs GeoAssetsServer:BaseUrl or an absolute MapInterop:MapLibre:StyleUrl.");
        var tileOrigin = new Uri(MapLibreHostPage.OriginOf(styleUrl));
        services.AddSingleton(MauiHostPage.RewrittenBy(html => MapLibreHostPage.WidenContentSecurityPolicy(html, tileOrigin)));

        var longPressConfigured = section.GetSection("MapLibre:LongPressContextMenu").Value is not null;
        services.PostConfigure<MapInteropOptions>(o =>
        {
            o.MapLibre ??= new MapLibreOptions();
            if (string.IsNullOrWhiteSpace(o.MapLibre.LibraryBaseUrl)) o.MapLibre.LibraryBaseUrl = LibraryBaseUrl;
            if (!longPressConfigured) o.MapLibre.LongPressContextMenu = true;
        });

        services.AddScoped<IMapTileLayerInterop>(sp => sp.GetRequiredService<MapInteropService>());
        services.AddScoped<IMapLocalVectorTileLayerInterop>(sp => sp.GetRequiredService<MapInteropService>());
        return services;
    }
}
