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
/// Not yet consumed anywhere — nothing force-resolves this Scoped service, so it never actually
/// runs today. XD01-84 wires it in and removes the old <c>@ref</c>-based path once this is
/// proven to cover the same behavior.
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
                foreach (var f in entry.Provider.GetAll())
                    await _mapInterop.RenderFeatureAsync(_mapContext.MapDivId, f);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render newly-connected provider '{Name}' onto the map", entry.Name);
        }
    }

    public void Dispose() => _pool.EntryAdded -= OnEntryAdded;
}
