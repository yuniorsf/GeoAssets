using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.Gdal.FileGeodatabase;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.Gdal;

public static class FileGeodatabaseServiceExtensions
{
    /// <summary>
    /// Registers the GDAL-backed ESRI File Geodatabase export service (XD01-225) for a host that references this
    /// project statically (GeoAssets.FieldService.Server) — what the <c>gdal-cloud-native</c> module registers too, so a
    /// host never needs both (XD01-437).
    /// </summary>
    public static IServiceCollection AddGeoAssetsFileGeodatabaseExport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IFileGeodatabaseExportService, FileGeodatabaseExportService>();
        return services;
    }
}
