namespace GeoAssets.Core.Rendering;

/// <summary>
/// A tile source that can say what it holds (ADR-002 D6, XD01-459): a plugin's deployment-wide <see cref="IVectorTileSource"/>
/// or <see cref="IRasterTileSource"/>, registered as a keyed service under its id, implements it so a host can publish and
/// route it without knowing the plugin — its URLs' version, the layers to request and the zooms and bounds it covers. A
/// shared contract in GeoAssets.Core, so the plugin's implementation and the host's endpoint meet in the default load
/// context. A host serves no keyed source that doesn't implement it.
/// </summary>
public interface IDescribedTileSource
{
    /// <summary>
    /// What the source holds now. Cheap to call again (the host calls it on every tile and <c>style.json</c> request): a
    /// source reads what it needs once and re-checks only whether its file changed.
    /// </summary>
    Task<TileSourceDescription> DescribeAsync(CancellationToken ct);
}
