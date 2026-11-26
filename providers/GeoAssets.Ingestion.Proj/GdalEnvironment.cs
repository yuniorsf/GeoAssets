using MaxRev.Gdal.Core;
using OSGeo.OSR;

namespace GeoAssets.Ingestion.Proj;

/// <summary>
/// Process-wide GDAL/PROJ set-up: <c>GdalBase.ConfigureAll()</c> once (guarded like
/// <c>FileGeodatabaseExportService</c>), and PROJ search-path changes, which are global to the process.
/// </summary>
internal static class GdalEnvironment
{
    private static readonly Lock ConfigureLock = new();
    private static bool _configured;
    private static string[] _basePaths = [];

    public static Lock SyncRoot => ConfigureLock;

    /// <summary>
    /// Configures GDAL if needed and prepends <paramref name="directory"/> to PROJ's search paths (once). Returns the
    /// search paths PROJ had before any directory was prepended, so a factory's grid lookup never depends on another
    /// factory's options.
    /// </summary>
    public static IReadOnlyList<string> EnsureConfigured(string? directory)
    {
        lock (ConfigureLock)
        {
            if (!_configured)
            {
                GdalBase.ConfigureAll();
                _basePaths = Osr.GetPROJSearchPaths() ?? [];
                _configured = true;
            }

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

            return _basePaths;
        }
    }
}
