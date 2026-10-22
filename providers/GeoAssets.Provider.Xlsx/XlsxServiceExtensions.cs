using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.Xlsx;

/// <summary>DI registration helpers for the GeoAssets.Provider.Xlsx Excel-import support.</summary>
public static class XlsxServiceExtensions
{
    /// <summary>Registers <see cref="IWorkbookReader"/>.</summary>
    public static IServiceCollection AddGeoAssetsXlsx(this IServiceCollection services)
    {
        services.AddSingleton<IWorkbookReader, ClosedXmlWorkbookReader>();
        return services;
    }
}
