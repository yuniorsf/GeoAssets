using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// Draws a provider entry whose server also serves its features as tiles (ADR-002 D6, XD01-436): the kernel's
/// <c>/tiles/style.json</c> (XD01-434) is the display source, and the discrete GeoJSON features only what tiles can't be.
/// <para>
/// <b>Which entries.</b> A provider declaring <see cref="ProviderRenderCapabilities.HybridLod"/> (a GeoAssets REST
/// server) whose <see cref="IRemoteAssetProvider.BaseAddress"/> has the origin of the map's tile style
/// (<see cref="MapLibreOptions.StyleUrl"/> under the gateway) — on a map in <see cref="MapRenderMode.MapLibre"/>, or in a
/// Leaflet mode with <see cref="MapInteropOptions.LeafletTileSplit"/> on, and only when the host registers
/// <see cref="IMapTileLayerInterop"/>. Anything else it declines, and <see cref="VectorFeatureMapLayerRenderer"/> draws it.
/// It asks first in the chain (<see cref="MapLayerRendererPrecedence.Refining"/>). While the style can't be read it draws
/// as the vector renderer does, and tries the style again at the next whole-layer render.
/// </para>
/// <para>
/// <b>MapLibre: tiles are the display.</b> A kernel feature nobody is editing is never fetched or drawn as GeoJSON — at
/// any zoom it is its tile's (raster, aggregate or vector tier), clicked, hovered and snapped to through the tiles. The
/// GeoJSON overlay holds only the <i>selected</i> feature (<see cref="IFeatureSelectionState"/>, fetched by id through
/// <see cref="IAssetProvider.GetById"/> — the catalog is never enumerated) and the features this client <i>wrote</i>
/// (added or updated), and the tile copy of each of them — and of each feature it deleted — is hidden by id
/// (<see cref="IMapTileLayerInterop.SetHiddenTileFeaturesAsync"/>), so nothing is drawn twice. A written or deleted
/// feature stays so until a re-read of the style that started after the write reports another <c>{v}</c>
/// (<see cref="IMapTileLayerInterop.TileStyleRefreshed"/>, raised once that <c>{v}</c>'s tiles are on the map): then its
/// tile copy shows again first and its overlay goes, unless it is still selected.
/// </para>
/// <para>
/// <b>Leaflet (the interim split).</b> At the zooms of the style's raster tier (<see cref="MapTileStyleInfo.IsRasterZoom"/>)
/// the raster tiles are drawn and no feature is fetched — only the selected and written ones, as an overlay (a raster
/// can't hide one feature, so until the next <c>{v}</c> its old drawing may show under it). At every other zoom, and
/// with no raster tier at all, the features are drawn exactly as <see cref="VectorFeatureMapLayerRenderer"/> draws them.
/// </para>
/// <para>One instance per scope; not thread-safe beyond its own lock — renderer context only.</para>
/// </summary>
public sealed partial class HybridLodMapLayerRenderer : IProviderMapLayerRenderer, IDisposable
{
    private readonly IMapInterop _map;
    private readonly IMapTileLayerInterop? _tiles;
    private readonly IFeatureSelectionState? _selection;
    private readonly VectorFeatureMapLayerRenderer _vector;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly bool _mapLibre;
    private readonly bool _enabled;
    private readonly Uri? _styleUrl;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, MapState> _maps = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <param name="mapInterop">Draws the overlay and, through the vector renderer, the features.</param>
    /// <param name="pipeline">The vector renderer's whole-layer stream.</param>
    /// <param name="options">The render mode, the Leaflet split and the style URL.</param>
    /// <param name="tiles">The tile layers; none (a host that registers none) = this renderer declines everything.</param>
    /// <param name="selection">The selected feature, drawn as overlay; none = no selection overlay.</param>
    /// <param name="routes">The gateway a relative style URL is under; none = a relative style URL declines everything.</param>
    /// <param name="timeProvider">When a write happened, compared with when a style re-read started.</param>
    /// <param name="logger">Where an unreadable style is reported.</param>
    public HybridLodMapLayerRenderer(
        IMapInterop mapInterop,
        FeatureRenderPipeline pipeline,
        IOptions<MapInteropOptions> options,
        IMapTileLayerInterop? tiles = null,
        IFeatureSelectionState? selection = null,
        ApiScopeRoutes? routes = null,
        TimeProvider? timeProvider = null,
        ILogger<HybridLodMapLayerRenderer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mapInterop);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(options);
        _map = mapInterop;
        _vector = new VectorFeatureMapLayerRenderer(mapInterop, pipeline);
        _tiles = tiles;
        _selection = selection;
        _time = timeProvider ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;

        var value = options.Value;
        _mapLibre = value.RenderMode == MapRenderMode.MapLibre;
        _enabled = _mapLibre || value.LeafletTileSplit;
        var styleUrl = MapLibreOptions.ResolveStyleUrl(value.MapLibre?.StyleUrl, routes?.BaseUrl);
        _styleUrl = Uri.TryCreate(styleUrl, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https"
            ? absolute
            : null;

        if (_tiles is not null) _tiles.TileStyleRefreshed += OnTileStyleRefreshed;
        if (_selection is not null) _selection.Changed += OnSelectionChanged;
    }

    public bool ClearedWithFeatures => false;

    public int Precedence => MapLayerRendererPrecedence.Refining;

    public bool CanRender(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return _enabled && _tiles is not null && _styleUrl is not null
            && provider.GetRenderCapabilities().HasFlag(ProviderRenderCapabilities.HybridLod)
            && provider.UnwrapToConcrete() is IRemoteAssetProvider { BaseAddress: { } address }
            && IsServedBy(address, _styleUrl);
    }

    /// <summary>Whether a server at <paramref name="address"/> is the one serving <paramref name="styleUrl"/> (same
    /// scheme, host and port — the gateway routes both the API and <c>/tiles</c> to the kernel).</summary>
    public static bool IsServedBy(Uri address, Uri styleUrl)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(styleUrl);
        return address.IsAbsoluteUri && styleUrl.IsAbsoluteUri
            && Uri.Compare(address, styleUrl, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    /// <summary>Shows the tiles and draws the overlay — or, while the style can't be read and in the Leaflet split off
    /// its raster zooms, the whole layer as the vector renderer does. Answers how many discrete features were drawn.</summary>
    public async Task<int> RenderAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = Bind(request);
        var info = await ShowAsync(request.MapDivId);
        cancellationToken.ThrowIfCancellationRequested();
        Learn(state, info);

        if (info is null)
        {
            LogStyleUnreadable(_logger, request.MapDivId);
            return await _vector.RenderAsync(request, cancellationToken);
        }
        bool vectorPath;
        lock (_gate)
        {
            vectorPath = !DrawsOverlay(state);
            if (vectorPath) state.Drawn.Clear();
        }
        if (vectorPath) return await _vector.RenderAsync(request, cancellationToken);
        // The map was just cleared (or never drew this entry): every overlay feature is drawn again.
        return await SyncAsync(state, redrawAll: true, cancellationToken);
    }

    /// <summary>Takes the tiles off the map and the overlay — or the features the vector path drew — with them.</summary>
    public async Task RemoveAsync(MapLayerRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MapState? state;
        Binding? binding;
        bool lastEntry;
        lock (_gate)
        {
            _maps.TryGetValue(request.MapDivId, out state);
            binding = null;
            if (state is not null && state.Entries.Remove(request.Entry.Id, out var removed)) binding = removed;
            lastEntry = state is null || state.Entries.Count == 0;
            if (state is not null && lastEntry) state.Shown = false;
        }
        binding?.Unsubscribe();

        // The map has one tile style: it goes when no entry drawn from it is left.
        if (lastEntry && _tiles is not null) await _tiles.HideTileLayersAsync(request.MapDivId);
        // An entry this renderer never drew (hidden from the start, or already removed) has nothing else on the map.
        if (state is null || binding is null) return;

        string[] drawn;
        bool vectorPath;
        lock (_gate)
        {
            vectorPath = !DrawsOverlay(state);
            drawn = [.. state.Drawn];
            state.Drawn.Clear();
        }
        if (vectorPath)
        {
            await _vector.RemoveAsync(request, cancellationToken);
            return;
        }
        foreach (var id in drawn)
            await _map.RemoveFeatureAsync(request.MapDivId, id);
    }

    /// <summary>
    /// During a viewport pass: the overlay again (so the pass keeps it) — or, at a Leaflet zoom outside the raster tier
    /// and while the style can't be read, the viewport's features as the vector renderer draws them.
    /// </summary>
    public async Task<int> RenderViewportAsync(MapLayerRenderRequest request, ViewportQuery viewport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = Bind(request);
        bool shown;
        lock (_gate)
        {
            if (viewport.Zoom is { } zoom) state.Zoom = zoom;
            shown = state.Shown;
        }
        if (!shown) Learn(state, await ShowAsync(request.MapDivId));
        cancellationToken.ThrowIfCancellationRequested();

        bool vectorPath;
        lock (_gate)
        {
            vectorPath = !DrawsOverlay(state);
            if (vectorPath) state.Drawn.Clear();
        }
        return vectorPath
            ? await _vector.RenderViewportAsync(request, viewport, cancellationToken)
            : await SyncAsync(state, redrawAll: true, cancellationToken);
    }

    /// <summary>The style's answer for the map; null without a tile interop (the vector path then draws).</summary>
    private Task<MapTileStyleInfo?> ShowAsync(string divId) =>
        _tiles is null ? Task.FromResult<MapTileStyleInfo?>(null) : _tiles.ShowTileLayersAsync(divId);

    public void Dispose()
    {
        Binding[] bindings;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            bindings = [.. _maps.Values.SelectMany(m => m.Entries.Values)];
            _maps.Clear();
        }
        foreach (var binding in bindings) binding.Unsubscribe();
        if (_tiles is not null) _tiles.TileStyleRefreshed -= OnTileStyleRefreshed;
        if (_selection is not null) _selection.Changed -= OnSelectionChanged;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private MapState Bind(MapLayerRenderRequest request)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_maps.TryGetValue(request.MapDivId, out var state))
                _maps[request.MapDivId] = state = new MapState(request.MapDivId);

            if (state.Entries.TryGetValue(request.Entry.Id, out var binding))
            {
                binding.Source = request.Source;
            }
            else
            {
                // The entry's own provider raises its writes whether or not it is the active one.
                state.Entries[request.Entry.Id] = new Binding(
                    request.Entry.Provider, request.Source,
                    (_, feature) => OnWritten(state, feature.Id, deleted: false),
                    (_, featureId) => OnWritten(state, featureId, deleted: true));
            }
            return state;
        }
    }

    private void Learn(MapState state, MapTileStyleInfo? info)
    {
        lock (_gate)
        {
            state.Shown = true;
            state.Info = info;
            if (info is null) return;
            state.Version = info.Version;
            state.Zoom = info.Zoom;
        }
    }

    /// <summary>Whether this renderer draws the overlay itself (else the vector path draws every feature). Under the lock.</summary>
    private bool DrawsOverlay(MapState state) =>
        state.Info is { } info && (_mapLibre || info.IsRasterZoom(state.Zoom ?? info.Zoom));

    // ── Events ────────────────────────────────────────────────────────────────

    private void OnWritten(MapState state, string featureId, bool deleted)
    {
        if (string.IsNullOrEmpty(featureId)) return;
        lock (_gate)
        {
            var write = new PendingWrite(_time.GetUtcNow(), state.Version);
            if (deleted)
            {
                state.Written.Remove(featureId);
                state.Deleted[featureId] = write;
            }
            else
            {
                state.Deleted.Remove(featureId);
                state.Written[featureId] = write;
            }
        }
        _ = SyncSafelyAsync(state, deleted ? [] : [featureId]);
    }

    private void OnSelectionChanged()
    {
        MapState[] maps;
        lock (_gate) maps = [.. _maps.Values];
        var selectedId = _selection?.Selected?.Id;
        foreach (var state in maps)
            _ = SyncSafelyAsync(state, selectedId is null ? [] : [selectedId]);
    }

    private void OnTileStyleRefreshed(object? sender, MapTileStyleRefreshedEventArgs e) => _ = OnTileStyleRefreshedAsync(e);

    private async Task OnTileStyleRefreshedAsync(MapTileStyleRefreshedEventArgs e)
    {
        try
        {
            MapState? state;
            bool moved;
            lock (_gate)
            {
                if (!_maps.TryGetValue(e.DivId, out state) || e.Version is null) return;
                moved = !string.Equals(state.Version, e.Version, StringComparison.Ordinal);
                state.Version = e.Version;
                Release(state.Written, e);
                Release(state.Deleted, e);
            }
            // The Leaflet split's raster zooms are the style's: a new {style} may have moved them.
            if (moved && !_mapLibre && _tiles is not null && HasEntries(state))
                Learn(state, await _tiles.ShowTileLayersAsync(e.DivId));
            await SyncAsync(state, redrawAll: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogSyncFailed(_logger, e.DivId, ex);
        }
    }

    /// <summary>
    /// Drops every write a re-read that started after it saw another <c>{v}</c> for: its tiles carry it now. A re-read
    /// that started before a write can't carry it — its <c>{v}</c> becomes the one the write waits to see move.
    /// </summary>
    private static void Release(Dictionary<string, PendingWrite> writes, MapTileStyleRefreshedEventArgs e)
    {
        foreach (var (id, write) in writes.ToList())
        {
            if (e.StartedAt < write.At)
                writes[id] = write with { Version = e.Version };
            else if (!string.Equals(write.Version, e.Version, StringComparison.Ordinal))
                writes.Remove(id);
        }
    }

    private bool HasEntries(MapState state)
    {
        lock (_gate) return state.Entries.Count > 0;
    }

    // ── The overlay and the hidden tile copies ────────────────────────────────

    private async Task SyncSafelyAsync(MapState state, IReadOnlyCollection<string> redraw)
    {
        try
        {
            await SyncAsync(state, redrawAll: false, CancellationToken.None, redraw);
        }
        catch (Exception ex)
        {
            LogSyncFailed(_logger, state.DivId, ex);
        }
    }

    /// <summary>
    /// Brings the map's overlay and hidden tile copies in line with the selection and the pending writes: draws the overlay
    /// features it lacks (all of them with <paramref name="redrawAll"/>, plus <paramref name="redraw"/>), then hides their
    /// tile copies (and the deleted ones'), then removes the overlay features no longer wanted — so a feature is never
    /// drawn by neither, and its tile copy shows again before its overlay goes. Answers how many features it drew.
    /// </summary>
    private async Task<int> SyncAsync(
        MapState state, bool redrawAll, CancellationToken cancellationToken, IReadOnlyCollection<string>? redraw = null)
    {
        var plan = Plan(state, redrawAll, redraw ?? []);
        if (plan is null) return 0;

        foreach (var feature in plan.Draw)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _map.RenderFeatureAsync(state.DivId, feature);
        }
        if (plan.Hidden is { } hidden && _tiles is not null)
            await _tiles.SetHiddenTileFeaturesAsync(state.DivId, hidden);
        foreach (var id in plan.Remove)
            await _map.RemoveFeatureAsync(state.DivId, id);
        return plan.Draw.Count;
    }

    private sealed record SyncPlan(IReadOnlyList<GeoFeature> Draw, IReadOnlyCollection<string>? Hidden, IReadOnlyList<string> Remove);

    private SyncPlan? Plan(MapState state, bool redrawAll, IReadOnlyCollection<string> redraw)
    {
        lock (_gate)
        {
            if (_disposed || state.Entries.Count == 0) return null;
            if (!DrawsOverlay(state))
            {
                // The vector path draws every feature: nothing is the overlay's to draw or remove.
                state.Drawn.Clear();
                return null;
            }

            var wanted = new Dictionary<string, GeoFeature>(StringComparer.Ordinal);
            if (_selection?.Selected is { } selected && !_selection.IsNew) Want(state, wanted, selected.Id);
            foreach (var id in state.Written.Keys) Want(state, wanted, id);

            var draw = wanted.Values
                .Where(f => redrawAll || !state.Drawn.Contains(f.Id) || redraw.Contains(f.Id, StringComparer.Ordinal))
                .ToList();
            var remove = state.Drawn.Where(id => !wanted.ContainsKey(id)).ToList();
            state.Drawn.Clear();
            state.Drawn.UnionWith(wanted.Keys);

            IReadOnlyCollection<string>? hidden = null;
            if (_mapLibre)
            {
                var nextHidden = new HashSet<string>(wanted.Keys, StringComparer.Ordinal);
                nextHidden.UnionWith(state.Deleted.Keys);
                if (redrawAll || !nextHidden.SetEquals(state.Hidden))
                {
                    state.Hidden.Clear();
                    state.Hidden.UnionWith(nextHidden);
                    hidden = [.. nextHidden.Order(StringComparer.Ordinal)];
                }
            }
            return new SyncPlan(draw, hidden, remove);
        }
    }

    /// <summary>Adds the feature <paramref name="id"/> of one of the map's entries, read by id.</summary>
    private static void Want(MapState state, Dictionary<string, GeoFeature> wanted, string id)
    {
        if (wanted.ContainsKey(id)) return;
        foreach (var binding in state.Entries.Values)
        {
            if (binding.Source.GetById(id) is { } feature)
            {
                wanted[id] = feature;
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Map {DivId}: the kernel's tile style could not be read; its features are drawn as GeoJSON until it can")]
    private static partial void LogStyleUnreadable(ILogger logger, string divId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Map {DivId}: updating the tile overlay failed")]
    private static partial void LogSyncFailed(ILogger logger, string divId, Exception ex);
}
