using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services.Rendering;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Renders a newly-connected provider onto the map — the pool-event-driven replacement for
/// <c>ProviderPoolPanel.HandleProviderConnected</c>, which only worked via an <c>@ref</c> to a
/// specific rendered panel instance. Subscribing to <see cref="IProviderPool.EntryAdded"/>
/// instead means this keeps working once panel items are resolved generically (XD01-85), with
/// no way to hold a typed <c>@ref</c> to them.
///
/// Registered in both <c>Program.cs</c> (Web) and <c>MauiProgram.cs</c>, and force-resolved once
/// in <c>Index.razor</c>'s <c>OnInitializedAsync</c> (XD01-84) — a Scoped service is never
/// instantiated by DI registration alone, so something has to ask for it to start the
/// <see cref="IProviderPool.EntryAdded"/> subscription.
/// </summary>
public sealed class ProviderConnectionMapRenderer : IDisposable
{
    private readonly IProviderPool _pool;
    private readonly IMapInterop _mapInterop;
    private readonly ICurrentMapContext _mapContext;
    private readonly ProviderMapLayerRendererChain _renderers;
    private readonly ILogger<ProviderConnectionMapRenderer> _logger;

    public ProviderConnectionMapRenderer(
        IProviderPool pool,
        IMapInterop mapInterop,
        ICurrentMapContext mapContext,
        ProviderMapLayerRendererChain renderers,
        ILogger<ProviderConnectionMapRenderer> logger)
    {
        _pool       = pool;
        _mapInterop = mapInterop;
        _mapContext = mapContext;
        _renderers  = renderers;
        _logger     = logger;

        _pool.EntryAdded += OnEntryAdded;
    }

    // EntryAdded is a synchronous EventHandler<T>, but rendering is async — fire-and-forget
    // with its own try/catch, since there's no caller here to propagate an exception to.
    private void OnEntryAdded(object? sender, ProviderEntry entry) => _ = RenderConnectedProviderAsync(entry);

    private async Task RenderConnectedProviderAsync(ProviderEntry entry)
    {
        try
        {
            var renderer = _renderers.Resolve(entry.Provider);
            if (renderer is null)
            {
                _logger.LogDebug("No map layer renderer draws newly-connected provider '{Name}'", entry.Name);
                return;
            }

            var rendered = await renderer.RenderAsync(MapLayerRenderRequest.For(_mapContext.MapDivId, entry));

            // Without this, newly-connected features render wherever they geographically are,
            // which is very often nowhere near the map's current view (e.g. its default
            // Caribbean-centered startup position) — they're drawn, but invisible until the
            // user happens to pan/zoom to them. Fit the view to what was just connected instead.
            // A tile layer draws no discrete features, so there's nothing to fit to.
            if (rendered == 0) return;
            var bbox = UnionBoundingBox(entry.Provider.GetAll());
            if (bbox is not null)
                await _mapInterop.FitBoundsAsync(_mapContext.MapDivId, bbox);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render newly-connected provider '{Name}' onto the map", entry.Name);
        }
    }

    /// <summary>
    /// Unions every feature's bounding box (RFC 7946 §5 order: minLon, minLat, maxLon, maxLat)
    /// into one covering the whole set, or <c>null</c> if none have a geometry.
    /// </summary>
    private static double[]? UnionBoundingBox(IReadOnlyList<GeoFeature> features)
    {
        double minLon = double.PositiveInfinity, minLat = double.PositiveInfinity;
        double maxLon = double.NegativeInfinity, maxLat = double.NegativeInfinity;
        var any = false;

        foreach (var f in features)
        {
            if (f.Geometry is null) continue;
            var box = f.Geometry.GetBoundingBox();
            any    = true;
            minLon = Math.Min(minLon, box[0]);
            minLat = Math.Min(minLat, box[1]);
            maxLon = Math.Max(maxLon, box[2]);
            maxLat = Math.Max(maxLat, box[3]);
        }

        return any ? [minLon, minLat, maxLon, maxLat] : null;
    }

    public void Dispose() => _pool.EntryAdded -= OnEntryAdded;
}
