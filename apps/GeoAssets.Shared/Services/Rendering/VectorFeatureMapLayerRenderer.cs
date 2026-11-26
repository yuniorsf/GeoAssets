using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// Renders a provider's discrete features (<see cref="ProviderRenderCapabilities.VectorFeatures"/>)
/// as Leaflet vector layers (XD01-422). Whole-layer reads stream through
/// <see cref="FeatureRenderPipeline"/> in <see cref="ChunkSize"/>-feature chunks so a large provider
/// doesn't block the UI thread (XD01-160); a viewport change re-fetches only what's inside it.
/// </summary>
public sealed class VectorFeatureMapLayerRenderer(IMapInterop mapInterop, FeatureRenderPipeline pipeline)
    : IProviderMapLayerRenderer
{
    /// <summary>Feature count per <see cref="FeatureRenderPipeline.StreamAllAsync"/> chunk — coarser
    /// than <c>MapInteropService</c>'s own JS-batch chunking, which further subdivides each chunk
    /// (XD01-162).</summary>
    public const int ChunkSize = 1500;

    public bool ClearedWithFeatures => true;

    public bool CanRender(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetRenderCapabilities().HasFlag(ProviderRenderCapabilities.VectorFeatures);
    }

    public async Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var count = 0;
        await foreach (var chunk in pipeline.StreamAllAsync(request.Source, ChunkSize, cancellationToken))
        {
            await mapInterop.RenderFeatureBatchAsync(request.MapDivId, chunk);
            count += chunk.Count;
        }
        return count;
    }

    public async Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await foreach (var chunk in pipeline.StreamAllAsync(request.Source, ChunkSize, cancellationToken))
            foreach (var feature in chunk)
                await mapInterop.RemoveFeatureAsync(request.MapDivId, feature.Id);
    }

    /// <summary>
    /// Fetches and additively renders the entry's features inside <paramref name="viewport"/>.
    /// Prefers the raw-JSON chunked path: the provider streams the HTTP response body(s) as-is, JS
    /// parses each chunk natively and renders it immediately (no WASM JSON parsing bottleneck, no
    /// waiting for one giant payload before anything appears). Providers that don't support raw
    /// JSON (e.g. Postgres) yield zero chunks — see IAssetProvider's default
    /// GetInBoundsRawJsonChunksAsync — which is the signal to fall back to the JSON-element path.
    /// A provider that does support it never yields zero for a real (possibly empty) result: even
    /// an empty bbox response is a non-null "[]" chunk. Returns the number of raw chunks rendered,
    /// or the feature count on the fallback path.
    /// </summary>
    public async Task<int> RenderViewportAsync(MapLayerRenderRequest request, BoundingBox viewport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (minLon, minLat, maxLon, maxLat) = viewport;
        var provider = request.Source;

        var chunkCount = 0;
        await foreach (var chunk in provider.GetInBoundsRawJsonChunksAsync(minLon, minLat, maxLon, maxLat, cancellationToken))
        {
            await mapInterop.RenderFeatureBatchRawJsonAsync(request.MapDivId, chunk);
            chunkCount++;
        }
        if (chunkCount > 0) return chunkCount;

        // GetInBoundsJsonAsync has no CancellationToken overload (not every provider's fetch
        // supports mid-flight abort — e.g. a plain HTTP GET without one plumbed through), so a
        // superseded call here can't be aborted early, but it must not paint its (now stale)
        // result over whatever a newer pan already rendered.
        var features = await provider.GetInBoundsJsonAsync(minLon, minLat, maxLon, maxLat);
        if (cancellationToken.IsCancellationRequested) return 0;
        await mapInterop.RenderFeatureBatchAsync(request.MapDivId, features);
        return features.Count;
    }
}
