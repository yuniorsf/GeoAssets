using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Interfaces;
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
    private readonly ILogger<ProviderConnectionMapRenderer> _logger;

    public ProviderConnectionMapRenderer(
        IProviderPool pool,
        IMapInterop mapInterop,
        ICurrentMapContext mapContext,
        ILogger<ProviderConnectionMapRenderer> logger)
    {
        _pool       = pool;
        _mapInterop = mapInterop;
        _mapContext = mapContext;
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
            if (entry.Provider is IWmsProvider wms)
            {
                await _mapInterop.AddWmsLayerAsync(_mapContext.MapDivId, entry.Id.ToString(),
                    wms.WmsBaseUrl,
                    new WmsLayerOptions(Layers: wms.WmsLayerName, Format: wms.WmsFormat));
            }
            else
            {
                var features = entry.Provider.GetAll();
                foreach (var f in features)
                    await _mapInterop.RenderFeatureAsync(_mapContext.MapDivId, f);

                // Without this, newly-connected features render wherever they geographically are,
                // which is very often nowhere near the map's current view (e.g. its default
                // Caribbean-centered startup position) — they're drawn, but invisible until the
                // user happens to pan/zoom to them. Fit the view to what was just connected instead.
                var bbox = UnionBoundingBox(features);
                if (bbox is not null)
                    await _mapInterop.FitBoundsAsync(_mapContext.MapDivId, bbox);
            }
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
