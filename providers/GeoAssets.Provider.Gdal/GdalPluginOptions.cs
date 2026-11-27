using GeoAssets.Core.Rendering;
using Microsoft.Extensions.Configuration;

namespace GeoAssets.Provider.Gdal;

/// <summary>
/// The <c>gdal-cloud-native</c> module's configuration (XD01-437), section <see cref="SectionName"/>:
/// <code>
/// "GeoAssets": { "Gdal": {
///   "PMTiles": { "&lt;id&gt;": { "Path": "/data/basemap.pmtiles" } },
///   "Cog":     { "&lt;id&gt;": { "Path": "/data/orthophoto.tif" } } } }
/// </code>
/// Each entry becomes a keyed <see cref="IVectorTileSource"/> / <see cref="IRasterTileSource"/> under its id. Nothing
/// configured registers no tile source.
/// </summary>
public sealed class GdalPluginOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "GeoAssets:Gdal";

    private GdalPluginOptions(IReadOnlyList<GdalTileSourceOptions> pmTiles, IReadOnlyList<GdalTileSourceOptions> cog)
    {
        PMTilesArchives = pmTiles;
        CogRasters = cog;
    }

    /// <summary>The PMTiles archives (vector tiles).</summary>
    public IReadOnlyList<GdalTileSourceOptions> PMTilesArchives { get; }

    /// <summary>The Cloud Optimized GeoTIFFs (raster tiles).</summary>
    public IReadOnlyList<GdalTileSourceOptions> CogRasters { get; }

    /// <summary>Reads and validates the module's section of <paramref name="configuration"/> (fail fast, ADR-002 D7).</summary>
    /// <exception cref="InvalidOperationException">An id breaks <see cref="TileKeySegment"/>, a path is blank, or a local file doesn't exist.</exception>
    public static GdalPluginOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        return new GdalPluginOptions(
            ReadSources(section.GetSection("PMTiles"), allowGdalPaths: false),
            ReadSources(section.GetSection("Cog"), allowGdalPaths: true));
    }

    private static List<GdalTileSourceOptions> ReadSources(IConfigurationSection section, bool allowGdalPaths)
    {
        var sources = new List<GdalTileSourceOptions>();
        foreach (var entry in section.GetChildren())
        {
            var where = $"{SectionName}:{section.Key}:{entry.Key}";
            if (!TileKeySegment.IsValid(entry.Key))
                throw new InvalidOperationException($"'{where}': the id '{entry.Key}' is not a tile key segment (1 to {TileKeySegment.MaxLength} of A-Z a-z 0-9 . _ ~ -).");

            var path = entry["Path"];
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException($"'{where}:Path' is required.");

            if (!(allowGdalPaths && path.StartsWith("/vsi", StringComparison.Ordinal)))
            {
                path = System.IO.Path.GetFullPath(path, AppContext.BaseDirectory);
                if (!File.Exists(path))
                    throw new InvalidOperationException($"'{where}:Path': the file '{path}' does not exist.");
            }

            sources.Add(new GdalTileSourceOptions(entry.Key, path));
        }

        return sources;
    }
}
