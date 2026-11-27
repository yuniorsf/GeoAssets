using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The deployment-wide tile sources the kernel serves (XD01-459): every <see cref="IVectorTileSource"/> and
/// <see cref="IRasterTileSource"/> registered as a keyed service under a string key that is a <see cref="TileKeySegment"/>
/// — e.g. the <c>gdal-cloud-native</c> plugin's PMTiles archives and Cloud Optimized GeoTIFFs
/// (<c>GeoAssets:Gdal:PMTiles|Cog:{id}:Path</c>), loaded from the plugin directory. Read from the host's service
/// collection when it is first resolved — once every module and plugin has registered — and resolved through the Core
/// contracts under their keys alone, so the kernel never names a plugin. A source that is not an
/// <see cref="IDescribedTileSource"/> is not served (its URLs need a version, its requests the layers to ask for).
/// Singleton.
/// </summary>
public sealed class KeyedTileSources
{
    /// <summary>The sources registered under <paramref name="vectorIds"/> and <paramref name="rasterIds"/>.</summary>
    public KeyedTileSources(IEnumerable<string> vectorIds, IEnumerable<string> rasterIds)
    {
        ArgumentNullException.ThrowIfNull(vectorIds);
        ArgumentNullException.ThrowIfNull(rasterIds);

        VectorIds = Ids(vectorIds);
        RasterIds = Ids(rasterIds);
    }

    /// <summary>The keys of the keyed <see cref="IVectorTileSource"/> registrations, in order.</summary>
    public IReadOnlyList<string> VectorIds { get; }

    /// <summary>The keys of the keyed <see cref="IRasterTileSource"/> registrations, in order.</summary>
    public IReadOnlyList<string> RasterIds { get; }

    /// <summary>The keyed tile sources of <paramref name="services"/> (a key that is not a tile key segment is not one).</summary>
    public static KeyedTileSources From(IEnumerable<ServiceDescriptor> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var descriptors = services.Where(d => d.IsKeyedService && d.ServiceKey is string).ToList();
        return new KeyedTileSources(KeysOf<IVectorTileSource>(descriptors), KeysOf<IRasterTileSource>(descriptors));
    }

    /// <summary>The source of <paramref name="format"/> registered under <paramref name="id"/>, if it is one the kernel serves.</summary>
    public KeyedTileSource? Resolve(IServiceProvider services, string id, TileFormat format)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(id);

        if (!(format == TileFormat.Mvt ? VectorIds : RasterIds).Contains(id, StringComparer.Ordinal))
            return null;

        object? source = format == TileFormat.Mvt
            ? services.GetKeyedService<IVectorTileSource>(id)
            : services.GetKeyedService<IRasterTileSource>(id);
        return source is IDescribedTileSource described ? new KeyedTileSource(id, format, source, described) : null;
    }

    /// <summary>Every source the kernel serves: the rasters, then the vector ones, each in registration order.</summary>
    public IReadOnlyList<KeyedTileSource> ResolveAll(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return [.. RasterIds.Select(id => Resolve(services, id, TileFormat.Png))
            .Concat(VectorIds.Select(id => Resolve(services, id, TileFormat.Mvt)))
            .OfType<KeyedTileSource>()];
    }

    private static IEnumerable<string> KeysOf<TService>(List<ServiceDescriptor> descriptors) =>
        descriptors.Where(d => d.ServiceType == typeof(TService)).Select(d => (string)d.ServiceKey!);

    private static string[] Ids(IEnumerable<string> ids) => [.. ids.Where(TileKeySegment.IsValid).Distinct(StringComparer.Ordinal)];
}
