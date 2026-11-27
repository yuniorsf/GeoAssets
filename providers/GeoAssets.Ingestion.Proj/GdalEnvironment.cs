using GeoAssets.Provider.Gdal;
using OSGeo.OSR;

namespace GeoAssets.Ingestion.Proj;

/// <summary>
/// Process-wide GDAL/PROJ set-up: native GDAL through the one shared <see cref="GdalNativeInitializer"/> of
/// <c>GeoAssets.Provider.Gdal</c> (ADR-002 D5, XD01-437 — never a second <c>ConfigureAll</c> of its own), and PROJ
/// search-path changes, which are global to the process.
/// </summary>
internal static class GdalEnvironment
{
    private static readonly Lock ConfigureLock = new();

    public static Lock SyncRoot => ConfigureLock;

    /// <summary>
    /// Configures GDAL if needed and prepends <paramref name="directory"/> to PROJ's search paths (once). Returns the
    /// search paths PROJ had before any directory was prepended, so a factory's grid lookup never depends on another
    /// factory's options.
    /// </summary>
    public static IReadOnlyList<string> EnsureConfigured(string? directory)
    {
        GdalNativeInitializer.EnsureInitialized();
        lock (ConfigureLock)
        {
            var paths = (Osr.GetPROJSearchPaths() ?? []).ToList();
            if (!string.IsNullOrWhiteSpace(directory))
            {
                var full = Path.GetFullPath(directory);
                if (!paths.Contains(full, StringComparer.Ordinal))
                {
                    paths.Insert(0, full);
                    Osr.SetPROJSearchPaths([.. paths]);
                }
            }

            return GdalNativeInitializer.InitialProjSearchPaths;
        }
    }
}
