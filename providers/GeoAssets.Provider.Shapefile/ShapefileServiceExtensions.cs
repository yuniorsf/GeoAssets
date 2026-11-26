using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.Shapefile;

public static class ShapefileServiceExtensions
{
    /// <summary>
    /// Registers the Shapefile provider plugin and the Shapefile tools — the registration of a host that
    /// both opens Shapefile catalogs and exports/imports Shapefiles. Equivalent to calling
    /// <see cref="AddGeoAssetsShapefilePlugin"/> and <see cref="AddGeoAssetsShapefileTools"/>.
    /// </summary>
    public static IServiceCollection AddGeoAssetsShapefile(this IServiceCollection services)
        => services.AddGeoAssetsShapefilePlugin().AddGeoAssetsShapefileTools();

    /// <summary>
    /// Registers the Shapefile provider plugin (<see cref="IProviderPlugin"/>, id <c>shapefile</c>) so it appears
    /// in the boot dialog and pool panel connect flow as a "Shapefile (SHP)" option. Registers nothing else.
    /// </summary>
    public static IServiceCollection AddGeoAssetsShapefilePlugin(this IServiceCollection services)
    {
        services.AddSingleton<IProviderPlugin, ShapefileProviderPlugin>();
        return services;
    }

    /// <summary>
    /// Registers the Shapefile tools — <see cref="IShapefileExportService"/> and <see cref="IBdgdPonnotImportService"/> —
    /// for a host that writes or reads Shapefiles without offering Shapefile as a provider. Registers no
    /// <see cref="IProviderPlugin"/>.
    /// </summary>
    public static IServiceCollection AddGeoAssetsShapefileTools(this IServiceCollection services)
    {
        services.AddSingleton<IShapefileExportService, ShapefileExportService>();
        services.AddSingleton<IBdgdPonnotImportService, BdgdPonnotImportService>();
        return services;
    }
}
