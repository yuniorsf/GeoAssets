using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.FileGeodatabase;

public static class FileGeodatabaseServiceExtensions
{
    /// <summary>Registers the GDAL-backed ESRI File Geodatabase export service (XD01-225).</summary>
    public static IServiceCollection AddGeoAssetsFileGeodatabaseExport(this IServiceCollection services)
    {
        services.AddSingleton<IFileGeodatabaseExportService, FileGeodatabaseExportService>();
        return services;
    }
}
