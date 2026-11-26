using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// Renders one map's pool entries for its viewport (XD01-429). A new viewport at once supersedes the pass before it —
/// cancelling its fetches, so nothing it still receives is painted — and, when debounced, waits
/// <see cref="DebounceInterval"/> without a newer one before fetching. The fetch reaches <see cref="PaddingRatio"/>
/// past every edge of the view and carries its zoom. The pass diffs instead of clearing the map
/// (<see cref="IMapInterop.BeginViewportRenderAsync"/> … <see cref="IMapInterop.EndViewportRenderAsync"/>): what it
/// draws again keeps or replaces its layer, and only what it no longer draws is removed — and only when it completes,
/// so a superseded pass removes nothing.
/// <para>One per map, owned by its <c>MapContainer</c>. Not thread-safe — renderer context only.</para>
/// </summary>
public sealed class MapViewportRenderer(
    IMapInterop mapInterop,
    ProviderMapLayerRendererChain renderers,
    TimeProvider timeProvider) : IDisposable
{
    /// <summary>How long the view must stay put before its features are fetched: a zoom's <c>zoomend</c> and
    /// <c>moveend</c>, or a quick run of pans, become one fetch.</summary>
    public static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>How far past each edge of the view a fetch reaches, as a fraction of the view's width (west/east) and
    /// height (south/north).</summary>
    public const double PaddingRatio = 0.25;

    private CancellationTokenSource? _pass;

    /// <summary>
    /// Renders <paramref name="viewport"/> after <see cref="DebounceInterval"/> without a newer call. Answers what was
    /// drawn, or <c>null</c> when a newer viewport (or <see cref="Cancel"/>) superseded this one.
    /// <paramref name="requests"/> is read once the debounce is over, so entries opened or hidden meanwhile count.
    /// </summary>
    public Task<ViewportRenderOutcome?> RenderDebouncedAsync(
        string divId, ViewportQuery viewport, Func<IEnumerable<MapLayerRenderRequest>> requests) =>
        RunAsync(divId, viewport, requests, debounce: true);

    /// <summary>Renders <paramref name="viewport"/> at once (a collection change re-rendering the known view); otherwise
    /// as <see cref="RenderDebouncedAsync"/>.</summary>
    public Task<ViewportRenderOutcome?> RenderNowAsync(
        string divId, ViewportQuery viewport, Func<IEnumerable<MapLayerRenderRequest>> requests) =>
        RunAsync(divId, viewport, requests, debounce: false);

    /// <summary>Supersedes the pending or running pass without starting another.</summary>
    public void Cancel()
    {
        if (_pass is not { } pass) return;
        _pass = null;
        pass.Cancel();
        pass.Dispose();
    }

    public void Dispose()
    {
        _pass?.Cancel();
        _pass?.Dispose();
        _pass = null;
    }

    private async Task<ViewportRenderOutcome?> RunAsync(
        string divId, ViewportQuery viewport, Func<IEnumerable<MapLayerRenderRequest>> requests, bool debounce)
    {
        ArgumentNullException.ThrowIfNull(requests);
        Cancel();
        var pass = new CancellationTokenSource();
        _pass = pass;
        var ct = pass.Token;

        try
        {
            if (debounce) await Task.Delay(DebounceInterval, timeProvider, ct);

            var started = timeProvider.GetTimestamp();
            var fetched = viewport with { Bounds = viewport.Bounds.Padded(PaddingRatio) };
            var generation = await mapInterop.BeginViewportRenderAsync(divId);

            var count = 0;
            foreach (var request in requests().ToList())
            {
                ct.ThrowIfCancellationRequested();
                if (renderers.Resolve(request.Entry.Provider) is not { } renderer) continue;
                count += await renderer.RenderViewportAsync(request, fetched, ct);
            }

            ct.ThrowIfCancellationRequested();
            await mapInterop.EndViewportRenderAsync(divId, generation);
            return new ViewportRenderOutcome(count, timeProvider.GetElapsedTime(started));
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (ReferenceEquals(_pass, pass))
            {
                _pass = null;
                pass.Dispose();
            }
        }
    }
}
