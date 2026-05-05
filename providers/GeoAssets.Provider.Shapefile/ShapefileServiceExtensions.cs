using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.Shapefile;

public static class ShapefileServiceExtensions
{
    /// <summary>
    /// Registers the Shapefile provider plugin so it appears in the boot dialog
    /// and pool panel connect flow as a "Shapefile (SHP)" option.
    /// </summary>
    public static IServiceCollection AddGeoAssetsShapefile(this IServiceCollection services)
    {
        services.AddSingleton<IProviderPlugin, ShapefileProviderPlugin>();
        return services;
    }
}
