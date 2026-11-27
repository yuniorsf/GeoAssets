/**
 * GeoAssets — MapLibre GL JS render mode (MapRenderMode.MapLibre, XD01-435, ADR-002 D6).
 *
 * The same contract as geoassets.js (IMapInterop / IMapSelectionInterop, one function per call, same names and
 * arguments) and the same JS→.NET callbacks on MapContainer, drawn by MapLibre GL JS instead of Leaflet:
 *   • the kernel's tiles: /tiles/style.json (XD01-434) — its vector source `geoassets` (promoteId "id"), its optional
 *     raster source `geoassets-raster` and its per-layer minzoom/maxzoom — merged over an OpenStreetMap basemap. Every
 *     request under the style's directory carries `Authorization: Bearer …` (transformRequest), the token asked of
 *     MapContainer.GetTileAccessFromJs and read afresh for each request, refreshed before it expires and after a 401.
 *   • the tiles' {v}: refreshTileStyle re-reads style.json (MapInteropService calls it after this client's writes and
 *     periodically) and, when only the tile URLs changed, swaps them on the existing sources — no map reload, no clear;
 *     MapLibre keeps each old tile on screen until its replacement arrives.
 *   • the features the providers render (renderFeature / renderFeatureBatch, unchanged): one GeoJSON source over the
 *     tiles, with the viewport passes of XD01-429.
 *   • click, hover and context menu: GPU feature queries (queryRenderedFeatures) over the feature and tile layers — no
 *     per-feature DOM or SVG layer.
 *   • draw, edit and snap: terra-draw with its MapLibre adapter; drawn and edited geometries reach .NET as RFC 7946
 *     GeoJSON ([lon, lat]), through OnFeatureDrawnFromJs / OnFeatureEditedFromJs as in Leaflet mode.
 *
 * Loaded lazily: MapInteropService imports this module only in MapLibre mode, and initializeMap loads MapLibre GL JS
 * and terra-draw from the CDN (pinned, with SRI) only then — a Leaflet map pays nothing.
 *
 * Zooms cross the contract in Leaflet's 256 px scale (this map's zoom + 1). What this mode does not do as Leaflet does
 * is listed on MapLibreOptions (C#) and logged once by warnOnce when it is asked for; nothing throws for it.
 */

// ─── CDN pins (exact versions, Subresource Integrity) ────────────────────────────────────────────────────────────────

export const LIBRARIES = Object.freeze({
    maplibreCss: {
        href: 'https://unpkg.com/maplibre-gl@5.24.0/dist/maplibre-gl.css',
        integrity: 'sha384-uTttxo/aOKbdE5RlD/SPzSDoDmNvGlUYPjONi2MN/b7c9HPSvW07OIuyP7uL6jxK'
    },
    maplibre: {
        src: 'https://unpkg.com/maplibre-gl@5.24.0/dist/maplibre-gl.js',
        integrity: 'sha384-5+cfbwT0iiub6VsQAdn6yz16nr6sDiQoHx6tm4O8OVYXHYOxcffFmCJBL0dgdvGp',
        global: 'maplibregl'
    },
    terraDraw: {
        src: 'https://unpkg.com/terra-draw@1.37.0/dist/terra-draw.umd.js',
        integrity: 'sha384-wQvA/9LThAOw1s0hHoAdjN7Gc1DUGm1rkP2+F52CgSqTSli4ueOaxgvHaJjwAcXN',
        global: 'terraDraw'
    },
    terraDrawAdapter: {
        src: 'https://unpkg.com/terra-draw-maplibre-gl-adapter@1.5.0/dist/terra-draw-maplibre-gl-adapter.umd.js',
        integrity: 'sha384-tE70KbU/wTkvNmGjc6IWlRU5lZSL/JyIw8ok1AB90T4SEjwYPgspmMzw4JWEOCCu',
        global: 'terraDrawMaplibreGlAdapter'
    }
});

// ─── Constants ───────────────────────────────────────────────────────────────────────────────────────────────────────

export const TILE_VECTOR_SOURCE = 'geoassets';
export const TILE_RASTER_SOURCE = 'geoassets-raster';
export const STYLE_VERSION_KEY  = 'geoassets:version';

export const FEATURE_SOURCE = 'geoassets-features';
export const FEATURE_LAYERS = Object.freeze({
    fill:       'geoassets-features-fill',
    outline:    'geoassets-features-outline',
    line:       'geoassets-features-line',
    lineDashed: 'geoassets-features-line-dashed',
    point:      'geoassets-features-point'
});
const OUTLINE_SOURCE = 'geoassets-selection-outline';
const OUTLINE_LAYER  = 'geoassets-selection-outline';
const REFERENCE_PREFIX = 'geoassets-reference-';
const XYZ_PREFIX       = 'geoassets-xyz-';

const DEFAULT_COLOR = '#3388ff';
const HIT_TOLERANCE_PX = 4;
const SNAP_DISTANCE_PX = 15;     // Geoman's snapDistance in Leaflet mode
const FEATURE_FOCUS_ZOOM = 16;   // panToFeature's maxZoom, Leaflet scale
const TOKEN_REFRESH_MARGIN_MS = 60 * 1000;
const TOKEN_UNAVAILABLE_HOLD_MS = 30 * 1000;

// Per-feature visual states (XD01-309): the same table and CSS tokens as geoassets.js. Colour, opacity and width only.
const STATE_STYLES = Object.freeze({
    selected:    { token: '--map-state-selected',    fallback: '#89b4fa', weight: 5,    alpha: 1,    fillAlpha: 0.55 },
    highlighted: { token: '--map-state-highlighted', fallback: '#f9e2af', weight: 5,    alpha: 1,    fillAlpha: 0.55 },
    conflict:    { token: '--map-state-conflict',    fallback: '#f38ba8', weight: 5,    alpha: 1,    fillAlpha: 0.55 },
    disabled:    { token: '--map-state-disabled',    fallback: '#6c7086', weight: null, alpha: 0.25, fillAlpha: 0.1 },
    pending:     { token: '--map-state-pending',     fallback: '#cba6f7', weight: 5,    alpha: 1,    fillAlpha: 0.55 },
    done:        { token: '--map-state-done',        fallback: '#a6e3a1', weight: 5,    alpha: 1,    fillAlpha: 0.55 }
});

// The paint properties a state overrides, by layer type, and what it puts there.
const STATE_PAINT = Object.freeze({
    circle: {
        'circle-color':          (def, color) => color,
        'circle-stroke-color':   (def, color) => color,
        'circle-opacity':        def => def.fillAlpha,
        'circle-stroke-opacity': def => def.alpha,
        'circle-stroke-width':   def => def.weight
    },
    line: {
        'line-color':   (def, color) => color,
        'line-opacity': def => def.alpha,
        'line-width':   def => def.weight
    },
    fill: {
        'fill-color':   (def, color) => color,
        'fill-opacity': def => def.fillAlpha
    }
});

// The style spec's defaults, for a paint property a layer leaves out.
const PAINT_DEFAULTS = Object.freeze({
    'circle-color': '#000000', 'circle-stroke-color': '#000000', 'circle-opacity': 1, 'circle-stroke-opacity': 1,
    'circle-stroke-width': 0, 'line-color': '#000000', 'line-opacity': 1, 'line-width': 1, 'fill-color': '#000000',
    'fill-opacity': 1
});

const SELECTION_TOOLS = ['none', 'click', 'box', 'fence'];
const DRAW_MODES = Object.freeze({ Point: 'point', LineString: 'linestring', Polygon: 'polygon' });
const EDIT_ACTIONS = new Set(['dragFeature', 'dragCoordinate', 'dragCoordinateResize', 'deleteCoordinate', 'insertMidpoint', 'edit']);

// ─── Pure helpers (exported for node --test) ─────────────────────────────────────────────────────────────────────────

/** Leaflet's 256 px zoom → this map's (512 px) zoom, and back. */
export function toMapLibreZoom(zoom) { return Number(zoom) - 1; }
export function fromMapLibreZoom(zoom) { return Number(zoom) + 1; }

/** The tiles' {v} a style publishes, or null. */
export function tileVersionOf(style) {
    const v = style && style.metadata && style.metadata[STYLE_VERSION_KEY];
    return typeof v === 'string' && v.length > 0 ? v : null;
}

function withoutTiles(source) {
    const copy = Object.assign({}, source);
    delete copy.tiles;
    return copy;
}

/**
 * What it takes to go from the tile style on the map (`current`, null before the first) to `next`:
 *   'none'    — same {v}, same sources and layers;
 *   'tiles'   — only the sources' tile URLs differ (a new {v}): `tiles` lists each source to re-point, nothing else moves;
 *   'rebuild' — the layers or the sources themselves changed (a new {style}): the tile layers are replaced.
 */
export function planStyleUpdate(current, next) {
    if (!next || typeof next !== 'object') return { kind: 'none', tiles: [] };
    if (!current) return { kind: 'rebuild', tiles: [] };

    const currentSources = current.sources || {};
    const nextSources = next.sources || {};
    const currentIds = Object.keys(currentSources).sort();
    const nextIds = Object.keys(nextSources).sort();
    if (JSON.stringify(currentIds) !== JSON.stringify(nextIds)) return { kind: 'rebuild', tiles: [] };
    if (JSON.stringify(current.layers || []) !== JSON.stringify(next.layers || [])) return { kind: 'rebuild', tiles: [] };

    const tiles = [];
    for (const id of nextIds) {
        if (JSON.stringify(withoutTiles(currentSources[id])) !== JSON.stringify(withoutTiles(nextSources[id])))
            return { kind: 'rebuild', tiles: [] };
        if (JSON.stringify(currentSources[id].tiles || []) !== JSON.stringify(nextSources[id].tiles || []))
            tiles.push({ sourceId: id, tiles: (nextSources[id].tiles || []).slice() });
    }
    return { kind: tiles.length > 0 ? 'tiles' : 'none', tiles };
}

/** The directory of `url` (with a trailing slash): the requests under it get the token. */
export function tokenScopeOf(url) {
    try { return new URL('./', url).href; } catch { return null; }
}

/** Whether `url` is under `scope` (same origin, path below it). */
export function isUnder(url, scope) {
    if (!url || !scope) return false;
    try {
        const u = new URL(url);
        const s = new URL(scope);
        return u.origin === s.origin && u.pathname.startsWith(s.pathname);
    } catch {
        return false;
    }
}

/**
 * The feature ids by effective state — the transient highlight wins over the persistent registry — as
 * [{ name, ids }], each id in one group only (MapLibre's `match` refuses a label twice).
 */
export function stateGroups(featureStates, highlights) {
    const effective = new Map();
    featureStates.forEach((name, id) => { if (id) effective.set(String(id), name); });
    highlights.forEach(id => { if (id) effective.set(String(id), 'highlighted'); });
    const byName = new Map();
    effective.forEach((name, id) => {
        if (!byName.has(name)) byName.set(name, []);
        byName.get(name).push(id);
    });
    return [...byName.entries()].map(([name, ids]) => ({ name, ids }));
}

function usesZoom(value) {
    return JSON.stringify(value ?? null).includes('"zoom"');
}

/**
 * `base` (a paint value or expression) with each state group's value where the feature id matches. A group whose
 * value is null (a state that keeps the property, e.g. `disabled`'s width) keeps `base`. A zoom expression can't be
 * nested in `match`, so a property whose base uses zoom is left as it is.
 */
export function stateExpression(base, groups, valueFor) {
    if (!groups.length || usesZoom(base)) return base;
    const expression = ['match', ['to-string', ['id']]];
    for (const { name, ids } of groups) {
        const value = valueFor(name);
        expression.push(ids, value === null || value === undefined ? base : value);
    }
    expression.push(base);
    return expression;
}

/** A Leaflet dash array ("8 6", pixels) in MapLibre's line-width units, or null. */
export function dashArrayOf(dashArray, weight) {
    if (!dashArray) return null;
    const width = Number(weight) > 0 ? Number(weight) : 1;
    const parts = String(dashArray).split(/[\s,]+/).map(Number).filter(n => Number.isFinite(n) && n >= 0);
    return parts.length >= 2 ? parts.map(n => n / width) : null;
}

/** The resolved style of a feature (.NET's LayerResolver), else its type's colour — as geoassets.js resolves it. */
export function effectiveStyle(feature, colorMap, resolvedStyle) {
    if (resolvedStyle) {
        return {
            color: resolvedStyle.color, weight: resolvedStyle.weight, radius: resolvedStyle.radius,
            fillColor: resolvedStyle.fillColor, fillOpacity: resolvedStyle.fillOpacity,
            dashArray: resolvedStyle.dashArray || null, iconUrl: resolvedStyle.iconUrl || null
        };
    }
    const color = (colorMap && colorMap[feature?.properties?.assetTypeId]) || DEFAULT_COLOR;
    return { color, weight: 3, radius: 8, fillColor: color, fillOpacity: 0.35, dashArray: null, iconUrl: null };
}

/** The GeoJSON feature the feature source holds: its id promoted (`properties.id`) and its style as properties. */
export function toSourceFeature(feature, style) {
    const props = feature.properties || {};
    const id = String(feature.id);
    const properties = { id, assetTypeId: props.assetTypeId || 'default' };
    if (typeof props.name === 'string') properties.name = props.name;
    if (style.color) properties.__color = style.color;
    if (Number.isFinite(style.weight)) properties.__weight = style.weight;
    if (Number.isFinite(style.radius)) properties.__radius = style.radius;
    if (style.fillColor) properties.__fillColor = style.fillColor;
    if (Number.isFinite(style.fillOpacity)) properties.__fillOpacity = style.fillOpacity;
    const dash = dashArrayOf(style.dashArray, style.weight);
    if (dash) properties.__dash = dash;
    return { type: 'Feature', id, geometry: feature.geometry, properties };
}

/** A WMS GetMap URL template on the EPSG:3857 grid MapLibre draws. */
export function wmsTileUrl(wmsBaseUrl, options) {
    const version = options?.version ?? '1.1.1';
    const params = [
        ['SERVICE', 'WMS'], ['REQUEST', 'GetMap'], ['VERSION', version],
        ['LAYERS', options?.layers ?? 'geoassets:feature'], ['STYLES', ''],
        ['FORMAT', options?.format ?? 'image/png'], ['TRANSPARENT', String(options?.transparent ?? true).toUpperCase()],
        [version === '1.3.0' ? 'CRS' : 'SRS', 'EPSG:3857'], ['WIDTH', '256'], ['HEIGHT', '256']
    ].map(([k, v]) => k + '=' + encodeURIComponent(v)).join('&');
    const separator = wmsBaseUrl.includes('?') ? (/[?&]$/.test(wmsBaseUrl) ? '' : '&') : '?';
    return wmsBaseUrl + separator + params + '&BBOX={bbox-epsg-3857}';
}

/** Leaflet's `{s}` subdomain placeholder expanded to a, b and c (MapLibre has none). */
export function expandSubdomains(url) {
    return url.includes('{s}') ? ['a', 'b', 'c'].map(s => url.replace('{s}', s)) : [url];
}

/** Every position of a GeoJSON geometry, flattened. */
export function positionsOf(geometry) {
    const out = [];
    const walk = c => {
        if (!Array.isArray(c)) return;
        if (typeof c[0] === 'number') { if (c.length >= 2) out.push(c); return; }
        c.forEach(walk);
    };
    if (geometry?.type === 'GeometryCollection') (geometry.geometries || []).forEach(g => out.push(...positionsOf(g)));
    else walk(geometry?.coordinates);
    return out;
}

/** [west, south, east, north] of a geometry, or null. */
export function boundsOf(geometry) {
    const positions = positionsOf(geometry);
    if (positions.length === 0) return null;
    let w = Infinity, s = Infinity, e = -Infinity, n = -Infinity;
    for (const [x, y] of positions) { w = Math.min(w, x); s = Math.min(s, y); e = Math.max(e, x); n = Math.max(n, y); }
    return [w, s, e, n];
}

/**
 * The position nearest `point` (pixels) among `features`' vertices, within `maxDistance` pixels, or undefined.
 * `project` maps [lon, lat] to { x, y }; `accept` filters the features (snap scope).
 */
export function nearestVertex(point, features, project, maxDistance, accept) {
    let best;
    let bestDistance = maxDistance;
    for (const f of features) {
        if (accept && !accept(f)) continue;
        for (const position of positionsOf(f.geometry)) {
            const p = project(position);
            const d = Math.hypot(p.x - point.x, p.y - point.y);
            if (d <= bestDistance) { bestDistance = d; best = [position[0], position[1]]; }
        }
    }
    return best;
}

/** The GeoAssets id of a rendered feature (the tiles' and the feature source's promoted `id`). */
export function featureIdOf(feature) {
    const id = feature?.properties?.id ?? feature?.id;
    return id === undefined || id === null || id === '' ? null : String(id);
}

/** Rounds every position to `digits` decimals (terra-draw refuses more than its coordinate precision). */
export function roundGeometry(geometry, digits = 9) {
    const factor = 10 ** digits;
    const round = c => typeof c[0] === 'number' ? c.map(n => Math.round(n * factor) / factor) : c.map(round);
    return { type: geometry.type, coordinates: round(geometry.coordinates) };
}

// ─── Browser-only helpers ────────────────────────────────────────────────────────────────────────────────────────────

const _warned = new Set();
function warnOnce(key, message) {
    if (_warned.has(key)) return;
    _warned.add(key);
    console.warn('GeoAssets (MapLibre): ' + message);
}

const HEX_COLOR = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i;
let _tokenColors = { theme: null, byToken: {} };
function cssColor(token, fallback) {
    const theme = document.documentElement.getAttribute('data-bs-theme') || '';
    if (_tokenColors.theme !== theme) _tokenColors = { theme, byToken: {} };
    let color = _tokenColors.byToken[token];
    if (!color) {
        const css = getComputedStyle(document.documentElement).getPropertyValue(token).trim();
        color = HEX_COLOR.test(css) ? css : fallback;
        _tokenColors.byToken[token] = color;
    }
    return color;
}

let _libraries = null;
function loadScript(lib) {
    if (window[lib.global]) return Promise.resolve();
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = lib.src;
        script.integrity = lib.integrity;
        script.crossOrigin = 'anonymous';
        script.async = false;
        script.onload = () => window[lib.global] ? resolve() : reject(new Error('GeoAssets: ' + lib.src + ' defined no ' + lib.global));
        script.onerror = () => reject(new Error('GeoAssets: could not load ' + lib.src));
        document.head.appendChild(script);
    });
}

function loadStylesheet(lib) {
    if (document.querySelector('link[data-geoassets-maplibre]')) return;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = lib.href;
    link.integrity = lib.integrity;
    link.crossOrigin = 'anonymous';
    link.setAttribute('data-geoassets-maplibre', '');
    document.head.appendChild(link);
}

/** MapLibre GL JS, terra-draw and its adapter — from the CDN once, unless the page already has them. */
function ensureLibraries() {
    if (!_libraries) {
        loadStylesheet(LIBRARIES.maplibreCss);
        _libraries = Promise.all([loadScript(LIBRARIES.maplibre), loadScript(LIBRARIES.terraDraw)])
            .then(() => loadScript(LIBRARIES.terraDrawAdapter))
            .catch(err => { _libraries = null; throw err; });
    }
    return _libraries;
}

function nextFrame() {
    return new Promise(resolve => (typeof requestAnimationFrame === 'function' ? requestAnimationFrame : setTimeout)(() => resolve()));
}

// ─── Map state registry ──────────────────────────────────────────────────────────────────────────────────────────────

const _maps = {};
const _initializing = new Map();

function invoke(state, method, ...args) {
    if (!state.dotNetRef) return Promise.resolve();
    return state.dotNetRef.invokeMethodAsync(method, ...args).catch(err => console.warn('GeoAssets (MapLibre): ' + method + ' failed', err));
}

// ─── Bearer token for the kernel's tiles (same contract as geoassets.js' WMS layers, XD01-96) ────────────────────────

function createTileAccess(state, url) {
    let access = null;
    let pending = null;
    let generation = 0;

    function isCurrent(a) {
        if (!a) return false;
        if (!a.requiresToken) return true;
        if (!a.accessToken) return Date.now() - a.receivedAt < TOKEN_UNAVAILABLE_HOLD_MS;
        return !a.expiresOn || Date.parse(a.expiresOn) - TOKEN_REFRESH_MARGIN_MS > Date.now();
    }
    function stamp(answer) { return Object.assign({}, answer, { receivedAt: Date.now(), generation: ++generation }); }
    function unavailable() { return stamp({ requiresToken: true, accessToken: null, expiresOn: null }); }

    // refused: the answer a request was just refused with (401). When it was already replaced, the replacement is used.
    function get(refused) {
        const replace = refused !== undefined && access && access.generation === refused.generation;
        if (!replace && isCurrent(access)) return Promise.resolve(access);
        if (pending) return pending;
        if (!state.dotNetRef) return Promise.resolve(unavailable());
        pending = state.dotNetRef.invokeMethodAsync('GetTileAccessFromJs', url)
            .then(answer => stamp(answer || {}), () => unavailable())
            .then(answer => { access = answer; pending = null; return answer; });
        return pending;
    }

    /** The latest answer, for transformRequest (synchronous); starts a refresh when it is about to expire. */
    function current() {
        if (access && !isCurrent(access) && !pending) get();
        return access;
    }

    function reject(refused) {
        if (access && access.generation === refused.generation) access = unavailable();
    }

    return { get, current, reject };
}

const TOKEN_USE_LIMIT = 512;              // tile URLs remembered with the token answer they were sent with
const UNAUTHORIZED_REFRESH_HOLD_MS = 10 * 1000;

function transformRequest(ctx, url) {
    const state = ctx.state;
    if (state && state.tileAccess && state.tokenScope && isUnder(url, state.tokenScope)) {
        const access = state.tileAccess.current();
        if (access && access.accessToken) {
            state.tokenUse.delete(url);
            state.tokenUse.set(url, access);
            if (state.tokenUse.size > TOKEN_USE_LIMIT) state.tokenUse.delete(state.tokenUse.keys().next().value);
            return { url, headers: { Authorization: 'Bearer ' + access.accessToken } };
        }
    }
    return { url };
}

async function fetchStyle(state) {
    let access = await state.tileAccess.get();
    for (let attempt = 0; ; attempt++) {
        if (access.requiresToken && !access.accessToken) return { error: 'no access token is available for ' + state.styleUrl };
        const headers = access.accessToken ? { Authorization: 'Bearer ' + access.accessToken } : {};
        // no-cache: the browser revalidates its copy with the style's ETag (304 when {v} did not move).
        const response = await fetch(state.styleUrl, { headers, cache: 'no-cache', credentials: 'omit' });
        if (response.status === 401 && attempt === 0) {
            access = await state.tileAccess.get(access);
            continue;
        }
        if (response.status === 401) state.tileAccess.reject(access);
        if (!response.ok) return { error: state.styleUrl + ' answered HTTP ' + response.status };
        return { style: await response.json() };
    }
}

// ─── The kernel's tile style ─────────────────────────────────────────────────────────────────────────────────────────

function isTileSource(id) { return id === TILE_VECTOR_SOURCE || id === TILE_RASTER_SOURCE; }

function removeTileStyle(state) {
    const map = state.map;
    state.tileLayers.forEach(layer => { if (map.getLayer(layer.id)) map.removeLayer(layer.id); });
    state.tileLayers = [];
    Object.keys(state.tileStyle?.sources || {}).forEach(id => { if (map.getSource(id)) map.removeSource(id); });
}

function addTileStyle(state, style) {
    const map = state.map;
    Object.entries(style.sources || {}).forEach(([id, source]) => {
        if (!map.getSource(id)) map.addSource(id, JSON.parse(JSON.stringify(source)));
    });
    state.tileLayers = [];
    (style.layers || []).forEach(layer => {
        if (!layer || !isTileSource(layer.source)) return;
        const copy = JSON.parse(JSON.stringify(layer));
        map.addLayer(copy, FEATURE_LAYERS.fill);
        state.tileLayers.push(copy);
    });
}

/** Applies `next` as the map's tile style; answers its {v}. */
function applyTileStyle(state, next) {
    const plan = planStyleUpdate(state.tileStyle, next);
    if (plan.kind === 'tiles') {
        plan.tiles.forEach(({ sourceId, tiles }) => state.map.getSource(sourceId)?.setTiles(tiles));
    } else if (plan.kind === 'rebuild') {
        removeTileStyle(state);
        addTileStyle(state, next);
        applyVisibility(state);
        applyStates(state);
    }
    state.tileStyle = next;
    state.tileVersion = tileVersionOf(next);
    return state.tileVersion;
}

/** Re-reads the style; one read per map at a time (a call meanwhile shares it). Answers the current {v}, or null. */
function loadTileStyle(state) {
    if (!state.styleUrl) return Promise.resolve(null);
    if (state.styleLoad) return state.styleLoad;
    state.styleLoad = fetchStyle(state)
        .then(result => {
            if (_maps[state.divId] !== state) return null;
            if (result.error) { warnOnce('style:' + result.error, 'tile style not loaded — ' + result.error); return state.tileVersion; }
            return applyTileStyle(state, result.style);
        })
        .catch(err => { console.warn('GeoAssets (MapLibre): reading the tile style failed', err); return state.tileVersion; })
        .finally(() => { state.styleLoad = null; });
    return state.styleLoad;
}

/** Re-points the tile sources at their own URLs, so tiles refused with an old token are asked again. */
function reloadTileSources(state) {
    Object.entries(state.tileStyle?.sources || {}).forEach(([id, source]) => {
        const s = state.map.getSource(id);
        if (s && s.setTiles && source.tiles) s.setTiles(source.tiles.slice());
    });
}

/** Asks the refused tiles again, once per token answer. */
function retryTilesWith(state, access) {
    if (state.retriedWith === access.generation) return;
    state.retriedWith = access.generation;
    reloadTileSources(state);
}

/**
 * A tile was refused (401) with the token answer it was sent with: when a newer answer exists, the tiles are asked
 * again with it; otherwise a new token is asked for — at most once per UNAUTHORIZED_REFRESH_HOLD_MS, so a server that
 * refuses every token is not asked in a loop — and the tiles are asked again only when it is a different token.
 */
async function onTileUnauthorized(state, url) {
    const used = url ? state.tokenUse.get(url) : null;
    const current = state.tileAccess.current();
    if (!used || !current) return;
    if (current.generation !== used.generation) {
        if (current.accessToken) retryTilesWith(state, current);
        return;
    }
    if (state.unauthorizedRetry || Date.now() - state.lastUnauthorizedRefresh < UNAUTHORIZED_REFRESH_HOLD_MS) return;
    state.unauthorizedRetry = true;
    state.lastUnauthorizedRefresh = Date.now();
    try {
        const next = await state.tileAccess.get(used);
        if (next.generation === used.generation || !next.accessToken) return;
        if (next.accessToken === used.accessToken) { state.tileAccess.reject(next); return; }
        retryTilesWith(state, next);
    } finally {
        state.unauthorizedRetry = false;
    }
}

// ─── Feature, outline and reference layers ───────────────────────────────────────────────────────────────────────────

const GEOMETRY = Object.freeze({
    polygon: ['match', ['geometry-type'], ['Polygon', 'MultiPolygon'], true, false],
    line:    ['match', ['geometry-type'], ['LineString', 'MultiLineString'], true, false],
    point:   ['match', ['geometry-type'], ['Point', 'MultiPoint'], true, false]
});

function featureLayerDefinitions() {
    const color = ['coalesce', ['get', '__color'], DEFAULT_COLOR];
    const fillColor = ['coalesce', ['get', '__fillColor'], ['get', '__color'], DEFAULT_COLOR];
    const weight = ['coalesce', ['get', '__weight'], 3];
    return [
        { id: FEATURE_LAYERS.fill, type: 'fill', geometry: GEOMETRY.polygon,
          paint: { 'fill-color': fillColor, 'fill-opacity': ['coalesce', ['get', '__fillOpacity'], 0.35] } },
        { id: FEATURE_LAYERS.outline, type: 'line', geometry: GEOMETRY.polygon, extra: ['!', ['has', '__dash']],
          paint: { 'line-color': color, 'line-width': weight, 'line-opacity': 0.9 } },
        { id: FEATURE_LAYERS.line, type: 'line', geometry: GEOMETRY.line, extra: ['!', ['has', '__dash']],
          paint: { 'line-color': color, 'line-width': weight, 'line-opacity': 0.9 } },
        { id: FEATURE_LAYERS.lineDashed, type: 'line', geometry: ['any', GEOMETRY.line, GEOMETRY.polygon], extra: ['has', '__dash'],
          paint: { 'line-color': color, 'line-width': weight, 'line-opacity': 0.9, 'line-dasharray': ['array', 'number', ['get', '__dash']] } },
        { id: FEATURE_LAYERS.point, type: 'circle', geometry: GEOMETRY.point,
          paint: {
              'circle-radius': ['coalesce', ['get', '__radius'], 8], 'circle-color': fillColor,
              'circle-opacity': ['coalesce', ['get', '__fillOpacity'], 0.35], 'circle-stroke-color': color,
              'circle-stroke-width': weight, 'circle-stroke-opacity': 0.9
          } }
    ];
}

function addOverlayLayers(state) {
    const map = state.map;
    map.addSource(FEATURE_SOURCE, { type: 'geojson', data: { type: 'FeatureCollection', features: [] }, promoteId: 'id' });
    state.featureLayers = featureLayerDefinitions();
    state.featureLayers.forEach(def => {
        map.addLayer({ id: def.id, type: def.type, source: FEATURE_SOURCE, filter: featureFilter(state, def), paint: def.paint });
    });

    map.addSource(OUTLINE_SOURCE, { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({
        id: OUTLINE_LAYER, type: 'line', source: OUTLINE_SOURCE,
        paint: { 'line-color': cssColor('--map-selection-outline', '#89b4fa'), 'line-width': 3, 'line-dasharray': [8 / 3, 2] }
    });
}

/** The feature layers' filter: their geometry, minus the hidden types and the feature being edited. */
function featureFilter(state, def) {
    const parts = ['all', def.geometry];
    if (def.extra) parts.push(def.extra);
    const hidden = [...hiddenTypes(state)];
    if (hidden.length) parts.push(['!', ['in', ['get', 'assetTypeId'], ['literal', hidden]]]);
    if (state.editing) parts.push(['!=', ['get', 'id'], state.editing.featureId]);
    return parts;
}

/** The layers a click, hover, context menu or snap can hit: the features' and the tiles' (aggregates are no feature). */
function interactiveLayerIds(state) {
    const ids = state.featureLayers.map(d => d.id);
    state.tileLayers.forEach(layer => {
        if (layer.source === TILE_VECTOR_SOURCE && ['fill', 'line', 'circle'].includes(layer.type) && !layer.id.endsWith('-aggregate'))
            ids.push(layer.id);
    });
    return ids.filter(id => state.map.getLayer(id));
}

const _commitScheduled = new WeakMap();
function commitFeatures(state) {
    let pending = _commitScheduled.get(state);
    if (pending) return pending;
    pending = nextFrame().then(() => {
        _commitScheduled.delete(state);
        if (_maps[state.divId] !== state) return;
        const features = [];
        state.features.forEach(f => { if (f.geometry) features.push(f); });
        state.map.getSource(FEATURE_SOURCE)?.setData({ type: 'FeatureCollection', features });
    });
    _commitScheduled.set(state, pending);
    return pending;
}

// ─── Visibility (user toggle + zoom ranges, XD01-311) ────────────────────────────────────────────────────────────────

function leafletZoom(state) { return Math.round(fromMapLibreZoom(state.map.getZoom())); }

function hiddenTypes(state) {
    const hidden = new Set(state.hiddenTypes);
    if (state.zoomRanges.size > 0) {
        const zoom = leafletZoom(state);
        state.zoomRanges.forEach((range, typeId) => {
            if ((range.min !== null && zoom < range.min) || (range.max !== null && zoom > range.max)) hidden.add(typeId);
        });
    }
    return hidden;
}

function applyVisibility(state) {
    const map = state.map;
    const hidden = hiddenTypes(state);
    state.featureLayers.forEach(def => { if (map.getLayer(def.id)) map.setFilter(def.id, featureFilter(state, def)); });
    state.tileLayers.forEach(layer => {
        if (!layer['source-layer'] || !map.getLayer(layer.id)) return;
        map.setLayoutProperty(layer.id, 'visibility', hidden.has(layer['source-layer']) ? 'none' : 'visible');
    });
}

// ─── Feature states & highlight (XD01-309, XD01-152) ─────────────────────────────────────────────────────────────────

function basePaintOf(state, layerId, property) {
    const own = state.featureLayers.find(d => d.id === layerId)?.paint
        ?? state.tileLayers.find(l => l.id === layerId)?.paint ?? {};
    return own[property] !== undefined ? own[property] : PAINT_DEFAULTS[property];
}

function applyStates(state) {
    const map = state.map;
    const groups = stateGroups(state.featureStates, state.highlights);
    const layers = state.featureLayers.map(d => ({ id: d.id, type: d.type }))
        .concat(state.tileLayers.filter(l => l.source === TILE_VECTOR_SOURCE && !l.id.endsWith('-aggregate')).map(l => ({ id: l.id, type: l.type })));
    layers.forEach(({ id, type }) => {
        const properties = STATE_PAINT[type];
        if (!properties || !map.getLayer(id)) return;
        Object.entries(properties).forEach(([property, value]) => {
            const base = basePaintOf(state, id, property);
            map.setPaintProperty(id, property, stateExpression(base, groups,
                name => value(STATE_STYLES[name], cssColor(STATE_STYLES[name].token, STATE_STYLES[name].fallback))));
        });
    });
}

// ─── Hit testing ─────────────────────────────────────────────────────────────────────────────────────────────────────

function hitTest(state, point, onlyFeatures) {
    const layers = onlyFeatures ? state.featureLayers.map(d => d.id) : interactiveLayerIds(state);
    if (!layers.length) return null;
    const box = [[point.x - HIT_TOLERANCE_PX, point.y - HIT_TOLERANCE_PX], [point.x + HIT_TOLERANCE_PX, point.y + HIT_TOLERANCE_PX]];
    for (const feature of state.map.queryRenderedFeatures(box, { layers })) {
        const id = featureIdOf(feature);
        if (id) return { id, feature };
    }
    return null;
}

function isAreaTool(tool) { return tool === 'box' || tool === 'fence'; }
function isBusy(state) { return !!state.drawMode || isAreaTool(state.selectionTool) || !!state.editing; }

function bindPointerEvents(state) {
    const map = state.map;

    map.on('click', e => {
        if (isBusy(state)) return;
        const hit = hitTest(state, e.point);
        if (!hit) return;
        if (state.selectionTool === 'click') {
            invoke(state, 'OnSelectionClickFromJs', hit.id, !!e.originalEvent?.shiftKey);
            return;
        }
        invoke(state, 'OnFeatureClickedFromJs', hit.id);
    });

    map.on('contextmenu', e => {
        const hit = hitTest(state, e.point);
        if (!hit) return;
        e.preventDefault();
        e.originalEvent?.preventDefault();
        invoke(state, 'OnFeatureContextMenuFromJs', hit.id, e.originalEvent.clientX, e.originalEvent.clientY);
    });

    // A double-click on a feature the providers rendered edits it (terra-draw select mode) instead of zooming.
    map.on('dblclick', e => {
        if (isBusy(state)) return;
        const hit = hitTest(state, e.point, true);
        if (!hit || !state.features.has(hit.id)) return;
        e.preventDefault();
        startEdit(state, hit.id);
    });

    let hoverQueued = null;
    map.on('mousemove', e => {
        hoverQueued = e;
        if (state.hoverFrame) return;
        state.hoverFrame = true;
        nextFrame().then(() => {
            state.hoverFrame = false;
            if (_maps[state.divId] !== state || !hoverQueued) return;
            hover(state, hoverQueued);
            hoverQueued = null;
        });
    });
    map.on('mouseout', () => { hoverQueued = null; hover(state, null); });
}

function hover(state, e) {
    const hit = e && !isBusy(state) ? hitTest(state, e.point) : null;
    if (!isBusy(state)) state.map.getCanvas().style.cursor = hit ? 'pointer' : '';
    if (!hit) { state.tooltip?.remove(); return; }
    const text = document.createElement('span');
    text.textContent = hit.feature.properties?.name || hit.id;   // user data: a text node, never HTML (XD01-412)
    state.tooltip = state.tooltip || new window.maplibregl.Popup({ closeButton: false, closeOnClick: false, offset: 10 });
    state.tooltip.setLngLat(e.lngLat).setDOMContent(text);
    if (!state.tooltip.isOpen()) state.tooltip.addTo(state.map);
}

// ─── terra-draw: draw, edit, snap, box and fence ─────────────────────────────────────────────────────────────────────

function snapTo(state, event) {
    if (isAreaTool(state.selectionTool)) return undefined;
    const map = state.map;
    const layers = interactiveLayerIds(state);
    if (!layers.length) return undefined;
    const point = { x: event.containerX, y: event.containerY };
    const box = [[point.x - SNAP_DISTANCE_PX, point.y - SNAP_DISTANCE_PX], [point.x + SNAP_DISTANCE_PX, point.y + SNAP_DISTANCE_PX]];
    const candidates = map.queryRenderedFeatures(box, { layers });
    const scope = state.snapScope;
    return nearestVertex(point, candidates, p => map.project(p), SNAP_DISTANCE_PX,
        f => !scope || scope.has(String(f.properties?.assetTypeId ?? f.sourceLayer ?? '')));
}

function startDraw(state) {
    const TD = window.terraDraw;
    const Adapter = window.terraDrawMaplibreGlAdapter.TerraDrawMapLibreGLAdapter;
    const snapping = { toCustom: event => snapTo(state, event) };
    const editable = { feature: { draggable: true, coordinates: { midpoints: true, draggable: true, deletable: true } } };
    const draw = new TD.TerraDraw({
        adapter: new Adapter({ map: state.map }),
        modes: [
            new TD.TerraDrawPointMode({ snapping }),
            new TD.TerraDrawLineStringMode({ snapping }),
            new TD.TerraDrawPolygonMode({ snapping }),
            new TD.TerraDrawRectangleMode(),
            new TD.TerraDrawPolygonMode({ modeName: 'fence' }),
            new TD.TerraDrawSelectMode({
                flags: { point: { feature: { draggable: true } }, linestring: editable, polygon: editable }
            })
        ]
    });
    draw.start();
    draw.on('finish', (id, context) => onDrawFinish(state, id, context));
    // Leaving the edit (a click beside the feature, Escape) ends it once terra-draw finished its own handling.
    draw.on('deselect', id => setTimeout(() => { if (state.editing && state.editing.tdId === id) stopEdit(state); }, 0));
    state.draw = draw;
}

function onDrawFinish(state, id, context) {
    const draw = state.draw;
    const feature = draw.getSnapshotFeature(id);
    if (!feature) return;

    if (state.editing && state.editing.tdId === id) {
        if (EDIT_ACTIONS.has(context?.action))
            invoke(state, 'OnFeatureEditedFromJs', state.editing.featureId, JSON.stringify(feature.geometry));
        return;
    }

    if (context?.action !== 'draw') return;
    // The drawn shape leaves terra-draw's store once terra-draw finished its own handling of the event.
    const area = isAreaTool(state.selectionTool) && (context.mode === 'rectangle' || context.mode === 'fence');
    if (!area) state.drawMode = null;
    setTimeout(() => {
        if (_maps[state.divId] !== state) return;
        if (draw.hasFeature(id)) draw.removeFeatures([id]);
        if (!area && !state.drawMode && draw.getMode() === context.mode) draw.setMode('static');
    }, 0);

    // A shape drawn with a selection tool only describes an area (XD01-310): its own callback, and the tool stays armed.
    if (area) {
        invoke(state, 'OnSelectionGeometryFromJs', state.selectionTool, JSON.stringify(feature.geometry.coordinates), state.additive);
        return;
    }

    // One shape per enableDraw, as Leaflet-Geoman draws: .NET renders the feature it creates through renderFeature.
    invoke(state, 'OnFeatureDrawnFromJs', JSON.stringify({ type: 'Feature', properties: {}, geometry: feature.geometry }));
}

function startEdit(state, featureId) {
    const entry = state.features.get(featureId);
    const geometry = entry?.geometry;
    const mode = geometry && DRAW_MODES[geometry.type];
    if (!mode) {
        warnOnce('edit:' + (geometry?.type ?? 'none'), 'a ' + (geometry?.type ?? 'missing') + ' geometry is not editable.');
        return false;
    }
    stopEdit(state);
    const draw = state.draw;
    const tdId = draw.getFeatureId();
    const [result] = draw.addFeatures([{ id: tdId, type: 'Feature', geometry: roundGeometry(geometry), properties: { mode } }]);
    if (!result || !result.valid) {
        warnOnce('edit-invalid:' + featureId, 'feature ' + featureId + ' is not editable: ' + (result?.reason ?? 'refused by terra-draw'));
        return false;
    }
    state.editing = { featureId, tdId };
    applyVisibility(state);
    draw.setMode('select');
    draw.selectFeature(tdId);
    return true;
}

function stopEdit(state) {
    const editing = state.editing;
    if (!editing) return;
    state.editing = null;
    const draw = state.draw;
    if (draw.hasFeature(editing.tdId)) draw.removeFeatures([editing.tdId]);
    if (draw.getMode() === 'select') draw.setMode('static');
    applyVisibility(state);
}

/** Tracks the additive modifier (Shift) on every press, before terra-draw or MapLibre sees it. */
function bindSelectionInput(state) {
    const container = state.map.getContainer();
    const onPress = e => { state.additive = !!e.shiftKey; };
    container.addEventListener('mousedown', onPress, true);
    container.addEventListener('click', onPress, true);
    state.selectionInput = { container, onPress };
}

function unbindSelectionInput(state) {
    const input = state.selectionInput;
    if (!input) return;
    input.container.removeEventListener('mousedown', input.onPress, true);
    input.container.removeEventListener('click', input.onPress, true);
    state.selectionInput = null;
}

// ─── Public API: lifecycle ───────────────────────────────────────────────────────────────────────────────────────────

function baseStyle(options) {
    const tiles = Array.isArray(options?.basemapTiles) ? options.basemapTiles
        : ['https://a.tile.openstreetmap.org/{z}/{x}/{y}.png', 'https://b.tile.openstreetmap.org/{z}/{x}/{y}.png', 'https://c.tile.openstreetmap.org/{z}/{x}/{y}.png'];
    const style = { version: 8, sources: {}, layers: [] };
    if (tiles.length) {
        style.sources.osm = {
            type: 'raster', tiles, tileSize: 256, maxzoom: 19,
            attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        };
        style.layers.push({ id: 'osm', type: 'raster', source: 'osm' });
    }
    return style;
}

/**
 * Creates the map in the element `divId` (idempotent). `options`: { styleUrl, gatewayBaseUrl } (MapLibreMapOptions);
 * a page may add `basemapTiles` (XYZ templates; [] = none). The tile style itself is read once registerHandlers gave
 * the map its .NET reference, which the token is asked of.
 */
async function initializeMap(divId, lat, lon, zoom, _renderMode, options) {
    if (_maps[divId]) return;
    if (_initializing.has(divId)) return _initializing.get(divId);

    const pending = (async () => {
        await ensureLibraries();
        const maplibregl = window.maplibregl;
        const ctx = { state: null };
        const map = new maplibregl.Map({
            container: divId,
            style: baseStyle(options),
            center: [lon, lat],
            zoom: toMapLibreZoom(zoom),
            attributionControl: { compact: true },
            transformRequest: url => transformRequest(ctx, url)
        });
        map.dragRotate.disable();
        map.touchZoomRotate.disableRotation();
        map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-left');
        await new Promise(resolve => map.once('load', resolve));

        const styleUrl = options?.styleUrl ? new URL(options.styleUrl, document.baseURI).href : null;
        const state = {
            divId, map,
            dotNetRef: null,
            styleUrl,
            tokenScope: styleUrl ? tokenScopeOf(styleUrl) : null,
            gatewayBaseUrl: options?.gatewayBaseUrl ? new URL(options.gatewayBaseUrl, document.baseURI).href : null,
            tileAccess: null,
            tileStyle: null,
            tileVersion: null,
            tileLayers: [],
            styleLoad: null,
            unauthorizedRetry: false,
            lastUnauthorizedRefresh: 0,
            retriedWith: null,
            tokenUse: new Map(),        // tile URL → the token answer transformRequest sent it with
            featureLayers: [],
            features: new Map(),        // featureId → source feature (toSourceFeature)
            renderGeneration: 0,
            viewportPass: null,
            featureStates: new Map(),
            highlights: new Set(),
            hiddenTypes: new Set(),
            zoomRanges: new Map(),
            snapScope: null,
            selectionTool: 'none',
            selectionSaved: null,
            selectionInput: null,
            additive: false,
            drawMode: null,
            editing: null,
            draw: null,
            rasterLayers: new Map(),    // layerId → { sourceId, layerId } of addTileLayer / addWmsLayer
            referenceLayers: new Map(), // layerId → [mapLayerId…]
            tooltip: null,
            hoverFrame: false
        };
        ctx.state = state;
        state.tileAccess = styleUrl ? createTileAccess(state, styleUrl) : null;

        addOverlayLayers(state);
        startDraw(state);
        bindPointerEvents(state);
        bindSelectionInput(state);
        map.on('zoomend', () => { if (state.zoomRanges.size > 0) applyVisibility(state); });
        map.on('error', e => {
            if (e?.error?.status === 401 && isTileSource(e.sourceId)) onTileUnauthorized(state, e.error.url);
        });
        _maps[divId] = state;
    })();

    _initializing.set(divId, pending);
    try { await pending; } finally { _initializing.delete(divId); }
}

function destroyMap(divId) {
    const state = _maps[divId];
    if (!state) return;
    delete _maps[divId];
    unbindSelectionInput(state);
    state.tooltip?.remove();
    try { state.draw?.stop(); } catch (e) { console.warn('GeoAssets (MapLibre): terra-draw did not stop cleanly', e); }
    state.map.remove();
}

function invalidateSize(divId) { _maps[divId]?.map.resize(); }

function setView(divId, lat, lon, zoom) {
    _maps[divId]?.map.jumpTo({ center: [lon, lat], zoom: toMapLibreZoom(zoom) });
}

/** Gives the map its .NET reference, reports the view as it settles and reads the kernel's tile style. */
function registerHandlers(divId, dotNetRef) {
    const state = _maps[divId];
    if (!state) return;
    state.dotNetRef = dotNetRef;

    let queued = false;
    state.map.on('moveend', () => {
        if (queued) return;
        queued = true;
        queueMicrotask(() => {
            queued = false;
            if (_maps[divId] !== state) return;
            const b = state.map.getBounds();
            invoke(state, 'OnViewportChangedFromJs', b.getWest(), b.getSouth(), b.getEast(), b.getNorth(), leafletZoom(state));
        });
    });

    loadTileStyle(state);
}

/** Re-reads style.json and follows a new {v} without reloading the map (XD01-435). Answers the current {v}. */
function refreshTileStyle(divId) {
    const state = _maps[divId];
    return state ? loadTileStyle(state) : Promise.resolve(null);
}

// ─── Public API: features ────────────────────────────────────────────────────────────────────────────────────────────

function parse(json) { return typeof json === 'string' ? JSON.parse(json) : json; }

function putFeature(state, feature, style) {
    if (!feature || feature.id === undefined || feature.id === null) return;
    const id = String(feature.id);
    if (state.viewportPass) state.viewportPass.seen.add(id);
    if (style.iconUrl) warnOnce('icon', 'icon styles are drawn as circles of their colour.');
    state.features.set(id, toSourceFeature(feature, style));
}

function renderFeature(divId, featureJson, colorMap, resolvedStyle) {
    const state = _maps[divId];
    if (!state) return undefined;
    const feature = parse(featureJson);
    putFeature(state, feature, effectiveStyle(feature, colorMap, resolvedStyle));
    return commitFeatures(state);
}

function renderAllFeatures(divId, featuresJson) {
    clearAllFeatures(divId);
    const state = _maps[divId];
    if (!state) return undefined;
    (parse(featuresJson) || []).forEach(f => putFeature(state, f, effectiveStyle(f, null, null)));
    return commitFeatures(state);
}

/** Adds a batch (resolves once drawn); a batch of a superseded viewport pass draws nothing (XD01-429). */
function renderFeatureBatch(divId, featuresJson, colorMap, styleMap) {
    const state = _maps[divId];
    if (!state) return undefined;
    const features = parse(featuresJson);
    if (!features?.length) return undefined;
    features.forEach(f => putFeature(state, f, effectiveStyle(f, colorMap, styleMap && styleMap[f.id])));
    return commitFeatures(state);
}

function removeFeature(divId, featureId) {
    const state = _maps[divId];
    if (!state) return;
    if (state.editing && state.editing.featureId === String(featureId)) stopEdit(state);
    if (state.features.delete(String(featureId))) commitFeatures(state);
}

function clearAllFeatures(divId) {
    const state = _maps[divId];
    if (!state) return;
    state.renderGeneration++;
    state.viewportPass = null;
    stopEdit(state);
    state.features.clear();
    commitFeatures(state);
}

function beginViewportRender(divId) {
    const state = _maps[divId];
    if (!state) return 0;
    state.renderGeneration++;
    state.viewportPass = { generation: state.renderGeneration, seen: new Set() };
    return state.renderGeneration;
}

function endViewportRender(divId, generation) {
    const state = _maps[divId];
    const pass = state?.viewportPass;
    if (!pass || pass.generation !== generation) return;
    state.viewportPass = null;
    let removed = false;
    [...state.features.keys()].forEach(id => {
        if (pass.seen.has(id) || (state.editing && state.editing.featureId === id)) return;
        state.features.delete(id);
        removed = true;
    });
    if (removed) commitFeatures(state);
}

// ─── Public API: draw, snap and selection tools ──────────────────────────────────────────────────────────────────────

function enableDraw(divId, geometryType) {
    const state = _maps[divId];
    const mode = DRAW_MODES[geometryType];
    if (!state || !mode) return;
    dropSelectionTool(state);
    stopEdit(state);
    state.drawMode = mode;
    state.draw.setMode(mode);
}

function disableDraw(divId) {
    const state = _maps[divId];
    if (!state || isAreaTool(state.selectionTool)) return;
    stopEdit(state);
    state.drawMode = null;
    state.draw.setMode('static');
}

function setSnapTargetLayer(divId, allowedTargetAssetTypeIds) {
    const state = _maps[divId];
    if (state) state.snapScope = new Set((allowedTargetAssetTypeIds || []).map(String));
}

function clearSnapTargetScope(divId) {
    const state = _maps[divId];
    if (state) state.snapScope = null;
}

function dropSelectionTool(state) {
    if (isAreaTool(state.selectionTool)) {
        state.draw.setMode('static');
        restoreAfterAreaTool(state);
    }
    state.selectionTool = 'none';
}

function prepareForAreaTool(state) {
    const boxZoom = state.map.boxZoom.isEnabled();
    state.selectionSaved = { boxZoom };
    if (boxZoom) state.map.boxZoom.disable();   // Shift+drag would fight the additive modifier
}

function restoreAfterAreaTool(state) {
    const saved = state.selectionSaved;
    if (!saved) return;
    state.selectionSaved = null;
    if (saved.boxZoom) state.map.boxZoom.enable();
}

/** 'click' | 'box' | 'fence' | 'none' (XD01-310); snapping is off while a box or fence is armed. */
function setSelectionTool(divId, tool) {
    const state = _maps[divId];
    if (!state) return;
    let next = String(tool ?? '').toLowerCase();
    if (!SELECTION_TOOLS.includes(next)) {
        console.warn(`GeoAssets (MapLibre): unknown selection tool '${tool}' treated as 'none'.`);
        next = 'none';
    }
    const wasArea = isAreaTool(state.selectionTool);
    const isArea = isAreaTool(next);
    if (wasArea || next !== 'none') {
        stopEdit(state);
        state.drawMode = null;
        state.draw.setMode('static');
    }
    if (wasArea && !isArea) restoreAfterAreaTool(state);
    if (!wasArea && isArea) prepareForAreaTool(state);
    state.selectionTool = next;
    if (isArea) state.draw.setMode(next === 'box' ? 'rectangle' : 'fence');
}

/** Edits a rendered feature with terra-draw's select mode (the double-click gesture's API). */
function editFeature(divId, featureId) {
    const state = _maps[divId];
    return !!state && !isBusy(state) && startEdit(state, String(featureId));
}

function stopEditing(divId) {
    const state = _maps[divId];
    if (state) stopEdit(state);
}

// ─── Public API: raster and WMS layers ───────────────────────────────────────────────────────────────────────────────

function firstOverlayLayer() { return FEATURE_LAYERS.fill; }

function addRasterLayer(state, layerId, tiles, options) {
    const sourceId = XYZ_PREFIX + layerId;
    state.map.addSource(sourceId, {
        type: 'raster', tiles, tileSize: 256,
        minzoom: Number.isFinite(options?.minZoom) ? options.minZoom : 0,
        maxzoom: Number.isFinite(options?.maxZoom) ? options.maxZoom : 19,
        attribution: options?.attribution || undefined
    });
    state.map.addLayer({
        id: sourceId, type: 'raster', source: sourceId,
        paint: { 'raster-opacity': Number.isFinite(options?.opacity) ? options.opacity : 1 }
    }, firstOverlayLayer());
    state.rasterLayers.set(layerId, { sourceId });
}

function addTileLayer(divId, layerId, url, options) {
    const state = _maps[divId];
    if (!state || state.rasterLayers.has(layerId)) return;
    addRasterLayer(state, layerId, expandSubdomains(url), options);
}

function removeRasterLayer(divId, layerId) {
    const state = _maps[divId];
    const entry = state?.rasterLayers.get(layerId);
    if (!entry) return;
    state.rasterLayers.delete(layerId);
    if (!entry.sourceId) return;
    if (state.map.getLayer(entry.sourceId)) state.map.removeLayer(entry.sourceId);
    if (state.map.getSource(entry.sourceId)) state.map.removeSource(entry.sourceId);
}

/**
 * A WMS layer on the EPSG:3857 grid. This app's own WMS (under the gateway) answers EPSG:4326 only, so it is not
 * drawn — the kernel's features come from its tiles in this mode.
 */
function addWmsLayer(divId, layerId, wmsBaseUrl, options) {
    const state = _maps[divId];
    if (!state || state.rasterLayers.has(layerId)) return;
    const url = new URL(wmsBaseUrl, document.baseURI).href;
    if (state.gatewayBaseUrl && isUnder(url, state.gatewayBaseUrl)) {
        warnOnce('wms-kernel', 'the kernel\'s WMS (EPSG:4326 only) is not drawn; its features come from /tiles/style.json.');
        state.rasterLayers.set(layerId, { sourceId: null });
        return;
    }
    addRasterLayer(state, layerId, [wmsTileUrl(url, options)], { maxZoom: options?.maxZoom, opacity: options?.opacity, attribution: options?.attribution });
}

// ─── Public API: visibility & zoom ranges ────────────────────────────────────────────────────────────────────────────

function setLayerVisibility(divId, assetTypeId, visible) {
    const state = _maps[divId];
    if (!state) return;
    visible ? state.hiddenTypes.delete(String(assetTypeId)) : state.hiddenTypes.add(String(assetTypeId));
    applyVisibility(state);
}

function zoomBound(value) {
    if (value === null || value === undefined) return null;
    const n = Number(value);
    return Number.isFinite(n) ? n : null;
}

function setLayerZoomRange(divId, assetTypeId, min, max) {
    const state = _maps[divId];
    if (!state) return;
    const range = { min: zoomBound(min), max: zoomBound(max) };
    if (range.min === null && range.max === null) state.zoomRanges.delete(String(assetTypeId));
    else state.zoomRanges.set(String(assetTypeId), range);
    applyVisibility(state);
}

// ─── Public API: selection outline & reference layers ────────────────────────────────────────────────────────────────

function isPosition(p) { return Array.isArray(p) && p.length >= 2 && Number.isFinite(p[0]) && Number.isFinite(p[1]); }

function showSelectionOutline(divId, rings) {
    const state = _maps[divId];
    if (!state) return;
    const drawable = (Array.isArray(rings) ? rings : []).filter(r => Array.isArray(r) && r.length >= 3 && r.every(isPosition));
    if (drawable.length === 0) {
        clearSelectionOutline(divId);
        console.warn('GeoAssets (MapLibre): selection outline ignored — no drawable ring.');
        return;
    }
    state.map.setPaintProperty(OUTLINE_LAYER, 'line-color', cssColor('--map-selection-outline', '#89b4fa'));
    state.map.getSource(OUTLINE_SOURCE).setData({ type: 'Feature', properties: {}, geometry: { type: 'Polygon', coordinates: drawable } });
}

function clearSelectionOutline(divId) {
    _maps[divId]?.map.getSource(OUTLINE_SOURCE)?.setData({ type: 'FeatureCollection', features: [] });
}

/** Another organization's features (XD01-331): non-interactive, muted, below the user's features. No labels here. */
function setReferenceLayer(divId, layerId, featuresJson, _options) {
    const state = _maps[divId];
    if (!state || !layerId) return;
    let features;
    try { features = parse(featuresJson); } catch (e) { console.warn('GeoAssets (MapLibre): reference layer ignored — not JSON.', e); return; }
    clearReferenceLayer(divId, layerId);
    warnOnce('reference-labels', 'reference layers are drawn without labels.');

    const sourceId = REFERENCE_PREFIX + layerId;
    const color = cssColor('--text-secondary', '#6c7086');
    const data = {
        type: 'FeatureCollection',
        features: (Array.isArray(features) ? features : []).filter(f => f && f.geometry)
            .map(f => ({ type: 'Feature', geometry: f.geometry, properties: {} }))
    };
    state.map.addSource(sourceId, { type: 'geojson', data });
    const layers = [
        { id: sourceId + '-fill', type: 'fill', filter: GEOMETRY.polygon, paint: { 'fill-color': color, 'fill-opacity': 0.12 } },
        { id: sourceId + '-line', type: 'line', filter: ['any', GEOMETRY.polygon, GEOMETRY.line], paint: { 'line-color': color, 'line-width': 1.5, 'line-dasharray': [4 / 1.5, 3 / 1.5] } },
        { id: sourceId + '-point', type: 'circle', filter: GEOMETRY.point, paint: { 'circle-radius': 5, 'circle-color': color, 'circle-opacity': 0.12, 'circle-stroke-color': color, 'circle-stroke-width': 1.5 } }
    ];
    layers.forEach(l => state.map.addLayer(Object.assign({ source: sourceId }, l), firstOverlayLayer()));
    state.referenceLayers.set(layerId, { sourceId, layerIds: layers.map(l => l.id) });
}

function clearReferenceLayer(divId, layerId) {
    const state = _maps[divId];
    const entry = state?.referenceLayers.get(layerId);
    if (!entry) return;
    entry.layerIds.forEach(id => { if (state.map.getLayer(id)) state.map.removeLayer(id); });
    if (state.map.getSource(entry.sourceId)) state.map.removeSource(entry.sourceId);
    state.referenceLayers.delete(layerId);
}

// ─── Public API: view ────────────────────────────────────────────────────────────────────────────────────────────────

function getViewBounds(divId) {
    const state = _maps[divId];
    if (!state) return null;
    const b = state.map.getBounds();
    return [b.getWest(), b.getSouth(), b.getEast(), b.getNorth()];
}

function fitBounds(divId, bbox) {
    const state = _maps[divId];
    if (state && bbox && bbox.length >= 4)
        state.map.fitBounds([[bbox[0], bbox[1]], [bbox[2], bbox[3]]], { padding: 20 });
}

function panToFeature(divId, featureId) {
    const state = _maps[divId];
    if (!state) return;
    const id = String(featureId);
    let geometry = state.features.get(id)?.geometry;
    if (!geometry) {
        const rendered = state.map.queryRenderedFeatures({ layers: interactiveLayerIds(state) }).find(f => featureIdOf(f) === id);
        geometry = rendered?.geometry;
    }
    const bounds = geometry && boundsOf(geometry);
    if (!bounds) return;
    const maxZoom = toMapLibreZoom(FEATURE_FOCUS_ZOOM);
    if (bounds[0] === bounds[2] && bounds[1] === bounds[3]) state.map.easeTo({ center: [bounds[0], bounds[1]], zoom: maxZoom });
    else state.map.fitBounds([[bounds[0], bounds[1]], [bounds[2], bounds[3]]], { padding: 20, maxZoom });
}

// ─── Public API: feature states & highlight ──────────────────────────────────────────────────────────────────────────

function setFeatureStates(divId, featureIds, stateName) {
    const state = _maps[divId];
    if (!state) return;
    const name = String(stateName || '').toLowerCase();
    if (name !== 'default' && !STATE_STYLES[name]) {
        console.warn(`GeoAssets (MapLibre): unknown feature state '${stateName}' ignored.`);
        return;
    }
    (featureIds || []).forEach(id => {
        if (name === 'default') state.featureStates.delete(String(id));
        else state.featureStates.set(String(id), name);
    });
    applyStates(state);
}

function clearFeatureStates(divId) {
    const state = _maps[divId];
    if (!state) return;
    state.featureStates.clear();
    applyStates(state);
}

function highlightFeature(divId, featureId) {
    const state = _maps[divId];
    if (!state) return;
    state.highlights.add(String(featureId));
    applyStates(state);
}

function clearHighlight(divId, featureId) {
    const state = _maps[divId];
    if (!state) return;
    state.highlights.delete(String(featureId));
    applyStates(state);
}

// ─── Export ──────────────────────────────────────────────────────────────────────────────────────────────────────────

const api = Object.freeze({
    initializeMap,
    destroyMap,
    invalidateSize,
    setView,
    registerHandlers,
    refreshTileStyle,
    enableDraw,
    disableDraw,
    setSnapTargetLayer,
    clearSnapTargetScope,
    setSelectionTool,
    editFeature,
    stopEditing,
    renderFeature,
    renderAllFeatures,
    renderFeatureBatch,
    removeFeature,
    clearAllFeatures,
    beginViewportRender,
    endViewportRender,
    addTileLayer,
    removeTileLayer: removeRasterLayer,
    addWmsLayer,
    removeWmsLayer: removeRasterLayer,
    setLayerVisibility,
    setLayerZoomRange,
    showSelectionOutline,
    clearSelectionOutline,
    setReferenceLayer,
    clearReferenceLayer,
    getViewBounds,
    fitBounds,
    panToFeature,
    setFeatureStates,
    clearFeatureStates,
    highlightFeature,
    clearHighlight
});

if (typeof window !== 'undefined') window.GeoAssetsMapLibre = api;

export default api;
