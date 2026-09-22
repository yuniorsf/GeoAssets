namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Marker interface for an <see cref="IAssetProvider"/> whose synchronous read surface
/// (<see cref="IAssetProvider.GetAll"/>, <see cref="IAssetProvider.GetWithin"/>, etc.) is
/// guaranteed non-blocking — the data is already resident in memory, with no lazy I/O behind it
/// (e.g. <c>ShapefileFeatureStore</c>). Providers that do real blocking work behind their sync
/// surface (e.g. Postgres's lazily-populated cache) must not implement this.
///
/// Checked via <see cref="Providers.AssetProviderExtensions.UnwrapToConcrete"/> at the one place
/// that dispatches reads off the render call stack (<see cref="Services.FeatureRenderPipeline"/>)
/// — see XD01-160. The classification only changes an internal telemetry tag there, not the
/// dispatch code path itself.
/// </summary>
public interface ISyncProvider
{
}
