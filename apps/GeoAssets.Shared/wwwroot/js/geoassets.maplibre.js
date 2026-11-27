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
 *     tiles, with the viewport passes of XD01-429. For the kernel's own entry (HybridLodMapLayerRenderer, XD01-436) that
 *     is only its selected and just-written features, whose tile copies setHiddenTileFeatures filters out until a {v}
 *     carrying the write is drawn (refreshTileStyle answers once the swapped tiles loaded); show/hideTileLayers follow
 *     the entry's Show/Hide.
 *   • click, hover and context menu: GPU feature queries (queryRenderedFeatures) over the feature and tile layers — no
 *     per-feature DOM or SVG layer.
 *   • draw, edit and snap: terra-draw with its MapLibre adapter; drawn and edited geometries reach .NET as RFC 7946
 *     GeoJSON ([lon, lat]), through OnFeatureDrawnFromJs / OnFeatureEditedFromJs as in Leaflet mode.
 *
 * Loaded lazily: MapInteropService imports this module only in MapLibre mode, and initializeMap loads MapLibre GL JS
 * and terra-draw from the CDN (pinned, with SRI) only then — a Leaflet map pays nothing. With options.libraryBaseUrl
 * (MAUI, XD01-460) they come from the app's own copies instead — MapLibre's CSP build, its worker read (SRI-checked)
 * into a blob: URL — so the map starts offline and the page names no script origin.
 *   • offline map packs (XD01-460): a raster pack is a raster source and a vector pack a vector source whose tiles are
 *     asked of .NET (the layer's LocalRasterTileReader / LocalVectorTileReader, GetTileFromJs) through the
 *     geoassets-pack:// protocol — read on the device, nothing fetched.
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

// The same libraries as the app's own copies (options.libraryBaseUrl, XD01-460), each at its unpkg path under that base:
// MapLibre's CSP build — its worker a file of its own instead of a blob built from the bundle — and the same files of
// terra-draw and its adapter as LIBRARIES (same SRI).
export const LOCAL_LIBRARIES = Object.freeze({
    maplibreCss: { path: 'maplibre-gl@5.24.0/dist/maplibre-gl.css', integrity: LIBRARIES.maplibreCss.integrity },
    maplibre: {
        path: 'maplibre-gl@5.24.0/dist/maplibre-gl-csp.js',
        integrity: 'sha384-+lDFpEYaS5ofD0qW/5dA+duOMWMSGs+utCY+wx9H/lS1i0WZ0bQH31giSmqYsxqR',
        global: 'maplibregl'
    },
    maplibreWorker: {
        path: 'maplibre-gl@5.24.0/dist/maplibre-gl-csp-worker.js',
        integrity: 'sha384-wBzwLUk46fiEnNOOlHXeylfQrL/SzyNqSD0gYdQJskF9cbBaNcRDYn6ZmSBW4l0z'
    },
    terraDraw: { path: 'terra-draw@1.37.0/dist/terra-draw.umd.js', integrity: LIBRARIES.terraDraw.integrity, global: 'terraDraw' },
    terraDrawAdapter: {
        path: 'terra-draw-maplibre-gl-adapter@1.5.0/dist/terra-draw-maplibre-gl-adapter.umd.js',
        integrity: LIBRARIES.terraDrawAdapter.integrity,
        global: 'terraDrawMaplibreGlAdapter'
    }
});

// ─── Constants ───────────────────────────────────────────────────────────────────────────────────────────────────────

export const TILE_VECTOR_SOURCE = 'geoassets';
export const TILE_RASTER_SOURCE = 'geoassets-raster';
// The deployment-wide keyed sources style.json lists below the kernel's tiles (XD01-459): geoassets-source-{mvt|png}-{id}.
export const TILE_KEYED_SOURCE_PREFIX = 'geoassets-source-';
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
const MAX_STYLE_ZOOM = 24;
const TILE_SETTLE_TIMEOUT_MS = 10 * 1000;   // how long a {v} swap waits for its tiles before reporting it anyway
const PENDING_OVERLAY_MS = 5 * 1000;        // how long an edit or pan waits for the overlay of a tile-only feature

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

// Offline map packs (XD01-460): the protocol their tiles are read through, and the image a raster tile the pack doesn't
// have is drawn as (a transparent 1×1 GIF, as geoassets.js draws it).
export const PACK_PROTOCOL = 'geoassets-pack';
const PACK_PREFIX = 'geoassets-pack-';
const EMPTY_IMAGE_BASE64 = 'R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';
const PACK_COLORS = Object.freeze({ point: '#e74c3c', line: '#3498db', polygon: '#2ecc71' });   // AssetType.Point/Line/Area

// A long press that opens a feature's context menu (options.longPressContextMenu, XD01-460).
const LONG_PRESS_MS = 500;
const LONG_PRESS_MOVE_PX = 10;
const CONTEXT_MENU_REPEAT_MS = 800;   // a platform contextmenu this close to a long press is the same gesture

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

/**
 * The style's raster tier (XD01-436): the tile zooms its raster layer covers, { minZoom, maxZoom } inclusive, or null
 * when it publishes none (the default LOD policy — vector at every zoom). `maxzoom` is exclusive in the style spec.
 */
export function rasterTierOf(style) {
    const layer = (style?.layers || []).find(l => l && l.source === TILE_RASTER_SOURCE && l.type === 'raster');
    if (!layer || !style?.sources?.[TILE_RASTER_SOURCE]) return null;
    const minZoom = Number.isFinite(layer.minzoom) ? layer.minzoom : 0;
    const maxZoom = Number.isFinite(layer.maxzoom) ? layer.maxzoom - 1 : MAX_STYLE_ZOOM;
    return maxZoom >= minZoom ? { minZoom, maxZoom } : null;
}

/** What showTileLayers answers .NET (MapTileStyleInfo): the {v}, the raster tier and the map's Leaflet-scale zoom. */
export function tileStyleInfo(style, zoom) {
    const tier = rasterTierOf(style);
    return {
        version: tileVersionOf(style),
        rasterMinZoom: tier ? tier.minZoom : null,
        rasterMaxZoom: tier ? tier.maxZoom : null,
        zoom: Math.round(Number(zoom) || 0)
    };
}

/**
 * A vector tile layer's filter with the hidden feature ids taken out (XD01-436): `base` (the style's own filter, or
 * none) and the tiles' promoted `id` not among `ids`. A hidden copy is neither drawn nor hit by queryRenderedFeatures.
 */
export function withoutHiddenIds(base, ids) {
    if (!ids || ids.length === 0) return base ?? null;
    const notHidden = ['!', ['in', ['to-string', ['get', 'id']], ['literal', ids.map(String)]]];
    return base ? ['all', base, notHidden] : notHidden;
}

/** [west, south, east, north] of the XYZ tile z/x/y, in degrees. */
export function tileBoundsOf(z, x, y) {
    const n = 2 ** z;
    const lon = c => c / n * 360 - 180;
    const lat = r => Math.atan(Math.sinh(Math.PI * (1 - 2 * r / n))) * 180 / Math.PI;
    return [lon(x), lat(y + 1), lon(x + 1), lat(y)];
}

/**
 * The positions of a feature MapLibre read from a vector tile that are real vertices: a tile's geometry is clipped at
 * its edge plus a buffer, and the clipped ends are vertices the feature doesn't have — so only the positions inside the
 * tile itself (`_z/_x/_y`, the tile MapLibre read it from) are snap candidates. A feature without its tile keeps all.
 */
export function tileVertices(feature) {
    const positions = positionsOf(feature?.geometry);
    const { _z: z, _x: x, _y: y } = feature || {};
    if (![z, x, y].every(Number.isInteger)) return positions;
    const [w, s, e, n] = tileBoundsOf(z, x, y);
    const eps = 1e-9;
    return positions.filter(([lon, lat]) => lon >= w - eps && lon <= e + eps && lat >= s - eps && lat <= n + eps);
}

/** Rounds every position to `digits` decimals (terra-draw refuses more than its coordinate precision). */
export function roundGeometry(geometry, digits = 9) {
    const factor = 10 ** digits;
    const round = c => typeof c[0] === 'number' ? c.map(n => Math.round(n * factor) / factor) : c.map(round);
    return { type: geometry.type, coordinates: round(geometry.coordinates) };
}

/**
 * The libraries initializeMap loads: LIBRARIES (the CDN) without `baseUrl`; with it, LOCAL_LIBRARIES resolved under it
 * (relative = under `documentBase`) — { maplibreCss: { href, integrity }, maplibre / terraDraw / terraDrawAdapter:
 * { src, integrity, global }, maplibreWorker: { src, integrity } }.
 */
export function librariesFor(baseUrl, documentBase) {
    if (typeof baseUrl !== 'string' || baseUrl.trim() === '') return LIBRARIES;
    const trimmed = baseUrl.trim();
    const base = new URL(trimmed.endsWith('/') ? trimmed : trimmed + '/', documentBase).href;
    const at = lib => new URL(lib.path, base).href;
    const script = lib => Object.freeze({ src: at(lib), integrity: lib.integrity, global: lib.global });
    return Object.freeze({
        maplibreCss: Object.freeze({ href: at(LOCAL_LIBRARIES.maplibreCss), integrity: LOCAL_LIBRARIES.maplibreCss.integrity }),
        maplibre: script(LOCAL_LIBRARIES.maplibre),
        maplibreWorker: Object.freeze({ src: at(LOCAL_LIBRARIES.maplibreWorker), integrity: LOCAL_LIBRARIES.maplibreWorker.integrity }),
        terraDraw: script(LOCAL_LIBRARIES.terraDraw),
        terraDrawAdapter: script(LOCAL_LIBRARIES.terraDrawAdapter)
    });
}

/** The tile URL template of the pack layer `key` (XD01-460): `geoassets-pack://{key}/{z}/{x}/{y}`. */
export function packTileTemplate(key) {
    return PACK_PROTOCOL + '://' + encodeURIComponent(String(key)) + '/{z}/{x}/{y}';
}

/** { key, z, x, y } of a pack tile URL, or null when it isn't one or names a tile outside the pyramid. */
export function parsePackTileUrl(url) {
    const m = /^geoassets-pack:\/\/([^/?#]+)\/(\d{1,2})\/(\d{1,10})\/(\d{1,10})$/.exec(String(url ?? ''));
    if (!m) return null;
    const [z, x, y] = [m[2], m[3], m[4]].map(Number);
    if (z > MAX_STYLE_ZOOM || x >= 2 ** z || y >= 2 ** z) return null;
    try {
        return { key: decodeURIComponent(m[1]), z, x, y };
    } catch {
        return null;
    }
}

/**
 * The names of the layers of a (decompressed) Mapbox Vector Tile, each once, in tile order — read from its protobuf
 * (tile field 3 = layer, layer field 1 = name), everything else skipped. A malformed tile answers what was read before.
 */
export function mvtLayerNames(bytes) {
    const buf = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes ?? []);
    const names = [];
    const varint = cursor => {
        let result = 0, shift = 0, b;
        do {
            if (cursor.pos >= cursor.end || shift > 49) throw new RangeError('bad varint');
            b = buf[cursor.pos++];
            result += (b & 0x7f) * 2 ** shift;
            shift += 7;
        } while (b & 0x80);
        return result;
    };
    const skip = (cursor, wire) => {
        if (wire === 0) varint(cursor);
        else if (wire === 1) cursor.pos += 8;
        else if (wire === 2) cursor.pos += varint(cursor);
        else if (wire === 5) cursor.pos += 4;
        else throw new RangeError('unsupported wire type ' + wire);
        if (cursor.pos > cursor.end) throw new RangeError('truncated field');
    };
    try {
        const tile = { pos: 0, end: buf.length };
        while (tile.pos < tile.end) {
            const key = varint(tile);
            if (key !== ((3 << 3) | 2)) { skip(tile, key & 7); continue; }
            const length = varint(tile);
            const layer = { pos: tile.pos, end: tile.pos + length };
            if (layer.end > buf.length) break;
            tile.pos = layer.end;
            while (layer.pos < layer.end) {
                const field = varint(layer);
                if (field === ((1 << 3) | 2)) {
                    const n = varint(layer);
                    if (layer.pos + n > layer.end) break;
                    const name = new TextDecoder().decode(buf.subarray(layer.pos, layer.pos + n));
                    if (!names.includes(name)) names.push(name);
                    break;
                }
                skip(layer, field & 7);
            }
        }
    } catch {
        // A malformed tile: the names read before the fault.
    }
    return names;
}

/**
 * The style layers that draw the MVT layer `sourceLayer` of the pack source `sourceId` (XD01-460): polygons filled and
 * outlined, lines, and points as circles, in `colors` ({ point, line, polygon }, else PACK_COLORS) with the defaults a
 * provider's features are drawn with — ids `${prefix}${n}-fill|outline|line|point`.
 */
export function packVectorLayers(prefix, sourceId, sourceLayer, n, colors) {
    const c = Object.assign({}, PACK_COLORS, colors || {});
    const base = { source: sourceId, 'source-layer': sourceLayer };
    return [
        Object.assign({ id: prefix + n + '-fill', type: 'fill', filter: GEOMETRY.polygon,
            paint: { 'fill-color': c.polygon, 'fill-opacity': 0.35 } }, base),
        Object.assign({ id: prefix + n + '-outline', type: 'line', filter: GEOMETRY.polygon,
            paint: { 'line-color': c.polygon, 'line-width': 3, 'line-opacity': 0.9 } }, base),
        Object.assign({ id: prefix + n + '-line', type: 'line', filter: GEOMETRY.line,
            paint: { 'line-color': c.line, 'line-width': 3, 'line-opacity': 0.9 } }, base),
        Object.assign({ id: prefix + n + '-point', type: 'circle', filter: GEOMETRY.point,
            paint: { 'circle-radius': 8, 'circle-color': c.point, 'circle-opacity': 0.35, 'circle-stroke-color': c.point,
                     'circle-stroke-width': 3, 'circle-stroke-opacity': 0.9 } }, base)
    ];
}

/** The bytes .NET answered for a tile (a Uint8Array over JS interop) as an ArrayBuffer of their own; null when none. */
export function toArrayBuffer(bytes) {
    if (!bytes) return null;
    if (bytes instanceof ArrayBuffer) return bytes.byteLength ? bytes : null;
    if (ArrayBuffer.isView(bytes))
        return bytes.byteLength ? bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) : null;
    return Array.isArray(bytes) && bytes.length ? new Uint8Array(bytes).buffer : null;
}

/** Whether a context menu at `now` is the one opened at `last` again (a long press and the platform's own, XD01-460). */
export function isRepeatedContextMenu(last, now) {
    return Number.isFinite(last) && now - last >= 0 && now - last < CONTEXT_MENU_REPEAT_MS;
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

/**
 * MapLibre's worker (the CSP build's, XD01-460) as a blob: URL of the SRI-checked file — a same-origin worker script
 * isn't served alike by every WebView, a blob: worker is what MapLibre's default build makes everywhere.
 */
let _workerSet = false;
async function useWorker(lib) {
    if (!lib || _workerSet) return;
    const response = await fetch(lib.src, { integrity: lib.integrity, credentials: 'same-origin' });
    if (!response.ok) throw new Error('GeoAssets: could not load ' + lib.src + ' (HTTP ' + response.status + ')');
    const blob = new Blob([await response.arrayBuffer()], { type: 'text/javascript' });
    window.maplibregl.setWorkerUrl(URL.createObjectURL(blob));
    _workerSet = true;
}

/**
 * MapLibre GL JS, terra-draw and its adapter — from the CDN, or the app's own copies (librariesFor), once, unless the
 * page already has them.
 */
function ensureLibraries(libs = LIBRARIES) {
    if (!_libraries) {
        loadStylesheet(libs.maplibreCss);
        _libraries = Promise.all([loadScript(libs.maplibre), loadScript(libs.terraDraw)])
            .then(() => useWorker(libs.maplibreWorker))
            .then(() => loadScript(libs.terraDrawAdapter))
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

/** Whether `id` is a source of the kernel's tile style: its own vector/raster tiles or a keyed source (XD01-459). */
export function isTileSource(id) {
    return id === TILE_VECTOR_SOURCE || id === TILE_RASTER_SOURCE || (typeof id === 'string' && id.startsWith(TILE_KEYED_SOURCE_PREFIX));
}

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

/**
 * Resolves once the tile sources reloaded after a swap (a 'sourcedata' of a tile source, then the map idle), or after
 * `timeoutMs` — a map that doesn't render (hidden) never goes idle.
 */
function whenTilesSettle(state, timeoutMs) {
    const map = state.map;
    return new Promise(resolve => {
        let reloading = false;
        let timer = null;
        const onData = e => { if (isTileSource(e.sourceId)) reloading = true; };
        const finish = () => {
            clearTimeout(timer);
            map.off('sourcedata', onData);
            map.off('idle', onIdle);
            resolve();
        };
        const onIdle = () => { if (reloading) finish(); };
        timer = setTimeout(finish, timeoutMs);
        map.on('sourcedata', onData);
        map.on('idle', onIdle);
    });
}

/**
 * Applies `next` as the map's tile style; answers its {v} — after a swap of a style already on the map, once the new
 * tiles are drawn (XD01-436: .NET then shows the tile copies of the features it wrote again, which must not flash
 * their old drawing).
 */
function applyTileStyle(state, next) {
    const previous = state.tileStyle;
    const plan = planStyleUpdate(previous, next);
    const settled = previous && plan.kind !== 'none' ? whenTilesSettle(state, TILE_SETTLE_TIMEOUT_MS) : null;
    if (plan.kind === 'tiles') {
        plan.tiles.forEach(({ sourceId, tiles }) => state.map.getSource(sourceId)?.setTiles(tiles));
    } else if (plan.kind === 'rebuild') {
        removeTileStyle(state);
        addTileStyle(state, next);
        applyVisibility(state);
        applyTileMask(state);
        applyStates(state);
    }
    state.tileStyle = next;
    state.tileVersion = tileVersionOf(next);
    const version = state.tileVersion;
    return settled ? settled.then(() => version) : version;
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
        resumePending(state);
    });
    _commitScheduled.set(state, pending);
    return pending;
}

/** The pending { id, until } when its feature is in the overlay now (and it didn't expire), else null; clears it. */
function takePending(state, key) {
    const pending = state[key];
    if (!pending) return null;
    if (Date.now() > pending.until) { state[key] = null; return null; }
    if (!state.features.has(pending.id)) return null;
    state[key] = null;
    return pending.id;
}

/** An edit or pan asked for a tile-only feature runs once its overlay arrived (XD01-436). */
function resumePending(state) {
    const pan = takePending(state, 'pendingPan');
    if (pan) panToFeature(state.divId, pan);
    const edit = takePending(state, 'pendingEdit');
    if (edit && !isBusy(state)) startEdit(state, edit);
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
        if (!map.getLayer(layer.id)) return;
        // hideTileLayers (XD01-436) takes every tile layer off, the raster one too; a type hidden takes off its own.
        const off = state.tilesHidden || (!!layer['source-layer'] && hidden.has(layer['source-layer']));
        map.setLayoutProperty(layer.id, 'visibility', off ? 'none' : 'visible');
    });
}

/** The vector tile layers whose features have ids (aggregates have none). */
function idTileLayers(state) {
    return state.tileLayers.filter(l => l.source === TILE_VECTOR_SOURCE && !l.id.endsWith('-aggregate'));
}

/** Filters the tile copies of setHiddenTileFeatures' ids out of the vector tile layers (XD01-436). */
function applyTileMask(state) {
    const map = state.map;
    idTileLayers(state).forEach(layer => {
        if (map.getLayer(layer.id)) map.setFilter(layer.id, withoutHiddenIds(layer.filter, state.hiddenTileIds));
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
        openContextMenu(state, hit, e.originalEvent.clientX, e.originalEvent.clientY);
    });

    // A double-click on a feature the providers rendered edits it (terra-draw select mode) instead of zooming. On a
    // tile-only one (XD01-436) it selects it — .NET then draws its GeoJSON overlay — and the edit starts once it arrives.
    map.on('dblclick', e => {
        if (isBusy(state)) return;
        const hit = hitTest(state, e.point, true);
        if (hit && state.features.has(hit.id)) {
            e.preventDefault();
            startEdit(state, hit.id);
            return;
        }
        const tileHit = hitTest(state, e.point);
        if (!tileHit) return;
        e.preventDefault();
        state.pendingEdit = { id: tileHit.id, until: Date.now() + PENDING_OVERLAY_MS };
        invoke(state, 'OnFeatureClickedFromJs', tileHit.id);
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

function openContextMenu(state, hit, clientX, clientY) {
    const now = Date.now();
    if (state.longPressContextMenu && isRepeatedContextMenu(state.lastContextMenuAt, now)) return;
    state.lastContextMenuAt = now;
    invoke(state, 'OnFeatureContextMenuFromJs', hit.id, clientX, clientY);
}

/**
 * A finger held still on a feature for LONG_PRESS_MS opens its context menu (XD01-460): WKWebView (iOS, Mac Catalyst)
 * fires no contextmenu for a long press, Android's WebView does — the two are one gesture (isRepeatedContextMenu). The
 * press is dropped by a second finger, a move past LONG_PRESS_MOVE_PX, a lift, or while drawing, editing or selecting
 * an area; the lift after one that opened the menu makes no click.
 */
function bindLongPress(state) {
    const map = state.map;
    let press = null;
    const cancel = () => { if (press) clearTimeout(press.timer); press = null; };
    map.on('touchstart', e => {
        cancel();
        const touches = e.originalEvent?.touches;
        if (!touches || touches.length !== 1 || isBusy(state)) return;
        const { clientX, clientY } = touches[0];
        const point = e.point;
        press = {
            point, opened: false,
            timer: setTimeout(() => {
                if (!press || _maps[state.divId] !== state) return;
                const hit = hitTest(state, point);
                if (!hit) { press = null; return; }
                press.opened = true;
                openContextMenu(state, hit, clientX, clientY);
            }, LONG_PRESS_MS)
        };
    });
    map.on('touchmove', e => {
        if (press && !press.opened && Math.hypot(e.point.x - press.point.x, e.point.y - press.point.y) > LONG_PRESS_MOVE_PX) cancel();
    });
    map.on('touchend', e => {
        if (press?.opened && e.originalEvent?.cancelable) e.originalEvent.preventDefault();
        cancel();
    });
    map.on('touchcancel', cancel);
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
    // XD01-436: an overlay feature snaps to its own geometry (the GeoJSON source's rendering is clipped and simplified);
    // the kernel's features to the MVT features rendered at this zoom — the vector tier's zooms, where a tile carries the
    // vertices themselves — minus the tile copies hidden for their overlay and the ends a tile's clipping made.
    const candidates = map.queryRenderedFeatures(box, { layers }).map(f => {
        if (f.source === FEATURE_SOURCE) {
            const own = state.features.get(featureIdOf(f));
            return own ? { geometry: own.geometry, properties: own.properties } : f;
        }
        return { geometry: { type: 'MultiPoint', coordinates: tileVertices(f) }, properties: f.properties, sourceLayer: f.sourceLayer };
    });
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
 * Creates the map in the element `divId` (idempotent). `options`: { styleUrl, gatewayBaseUrl, libraryBaseUrl,
 * longPressContextMenu } (MapLibreMapOptions);
 * a page may add `basemapTiles` (XYZ templates; [] = none). The tile style itself is read once registerHandlers gave
 * the map its .NET reference, which the token is asked of.
 */
async function initializeMap(divId, lat, lon, zoom, _renderMode, options) {
    if (_maps[divId]) return;
    if (_initializing.has(divId)) return _initializing.get(divId);

    const pending = (async () => {
        await ensureLibraries(librariesFor(options?.libraryBaseUrl, document.baseURI));
        ensurePackProtocol();
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
            tilesHidden: false,         // hideTileLayers (XD01-436)
            hiddenTileIds: [],          // setHiddenTileFeatures: ids whose tile copy is filtered out (XD01-436)
            pendingEdit: null,          // { id, until }: a tile-only feature double-clicked, edited once its overlay arrives
            pendingPan: null,           // { id, until }: panToFeature of a feature neither drawn nor rendered yet
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
            hoverFrame: false,
            packLayers: new Map(),      // layerId → { key, sourceId, layerIds, sourceLayers? } of the offline packs (XD01-460)
            longPressContextMenu: !!options?.longPressContextMenu,
            lastContextMenuAt: null
        };
        ctx.state = state;
        state.tileAccess = styleUrl ? createTileAccess(state, styleUrl) : null;

        addOverlayLayers(state);
        startDraw(state);
        bindPointerEvents(state);
        bindSelectionInput(state);
        if (state.longPressContextMenu) bindLongPress(state);
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
    state.packLayers.forEach(entry => _packTiles.delete(entry.key));
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

/**
 * Shows the kernel's tile layers (HybridLodMapLayerRenderer, XD01-436), reading the style first when the map has none
 * yet; answers tileStyleInfo, or null when the style can't be read. The style URL is the map's own (initializeMap).
 */
async function showTileLayers(divId, _styleUrl) {
    const state = _maps[divId];
    if (!state) return null;
    state.tilesHidden = false;
    if (!state.tileStyle) await loadTileStyle(state);
    if (_maps[divId] !== state) return null;
    applyVisibility(state);
    return state.tileStyle ? tileStyleInfo(state.tileStyle, leafletZoom(state)) : null;
}

function hideTileLayers(divId) {
    const state = _maps[divId];
    if (!state) return;
    state.tilesHidden = true;
    applyVisibility(state);
}

/** Hides the tile copy of each feature in `featureIds` (replacing the previous set): its overlay is drawn instead. */
function setHiddenTileFeatures(divId, featureIds) {
    const state = _maps[divId];
    if (!state) return;
    state.hiddenTileIds = [...new Set((featureIds || []).map(String))];
    applyTileMask(state);
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
 * drawn — the kernel's features come from its tiles in this mode — and neither is a distributor silo's (a layer with a
 * tenant domain, XD01-473).
 */
function addWmsLayer(divId, layerId, wmsBaseUrl, options) {
    const state = _maps[divId];
    if (!state || state.rasterLayers.has(layerId)) return;
    const url = new URL(wmsBaseUrl, document.baseURI).href;
    if (options?.tenantDomain) {
        warnOnce('wms-tenant', 'a WMS layer routed to a distributor silo (tenant domain) is drawn in the Leaflet modes only.');
        state.rasterLayers.set(layerId, { sourceId: null });
        return;
    }
    if (state.gatewayBaseUrl && isUnder(url, state.gatewayBaseUrl)) {
        warnOnce('wms-kernel', 'the kernel\'s WMS (EPSG:4326 only) is not drawn; its features come from /tiles/style.json.');
        state.rasterLayers.set(layerId, { sourceId: null });
        return;
    }
    addRasterLayer(state, layerId, [wmsTileUrl(url, options)], { maxZoom: options?.maxZoom, opacity: options?.opacity, attribution: options?.attribution });
}

// ─── Public API: offline map packs (XD01-460) ────────────────────────────────────────────────────────────────────────
// Each pack layer's tiles are asked of .NET (its reader's GetTileFromJs) through the geoassets-pack:// protocol, one key
// per layer on a map: read on the device, nothing fetched, so the page's CSP names nothing for them. MapLibre asks only
// the tiles of the view, within the pack's zooms (overzooming past its highest) and bounds.

const _packTiles = new Map();   // key → { state, reader, kind: 'raster' | 'vector', entry }
let _packSequence = 0;
let _packProtocol = false;
let _emptyImage = null;

function emptyImage() {
    if (!_emptyImage) _emptyImage = Uint8Array.from(atob(EMPTY_IMAGE_BASE64), ch => ch.charCodeAt(0)).buffer;
    return _emptyImage.slice(0);
}

async function loadPackTile(params) {
    const tile = parsePackTileUrl(params.url);
    const pack = tile && _packTiles.get(tile.key);
    if (!pack) throw new Error('GeoAssets (MapLibre): no offline pack serves ' + params.url);
    const data = toArrayBuffer(await pack.reader.invokeMethodAsync('GetTileFromJs', tile.z, tile.x, tile.y));
    if (pack.kind === 'raster') return { data: data ?? emptyImage() };
    if (data) discoverPackLayers(pack, data);
    return { data: data ?? new ArrayBuffer(0) };
}

function ensurePackProtocol() {
    if (_packProtocol) return;
    window.maplibregl.addProtocol(PACK_PROTOCOL, loadPackTile);
    _packProtocol = true;
}

function packBounds(bounds) {
    return Array.isArray(bounds) && bounds.length >= 4 && bounds.every(Number.isFinite) ? bounds.slice(0, 4) : undefined;
}

function packZoom(value, fallback) { return Number.isInteger(value) ? value : fallback; }

/** The first layer above the basemap: a raster pack goes over OpenStreetMap and the packs before it, under the rest. */
function aboveBasemap(state) {
    const layers = state.map.getStyle().layers || [];
    const first = layers.find(l => l.id !== 'osm' && !(l.id.startsWith(PACK_PREFIX) && l.type === 'raster'));
    return first ? first.id : undefined;
}

/** options = { contentType, minZoom, maxZoom, bounds: [west, south, east, north] | null } — the pack's. */
function addLocalRasterLayer(divId, layerId, reader, options) {
    const state = _maps[divId];
    if (!state || state.packLayers.has(layerId)) return;
    const key = 'p' + (++_packSequence);
    const sourceId = PACK_PREFIX + key;
    state.map.addSource(sourceId, {
        type: 'raster', tiles: [packTileTemplate(key)], tileSize: 256,
        minzoom: packZoom(options?.minZoom, 0), maxzoom: packZoom(options?.maxZoom, 19), bounds: packBounds(options?.bounds)
    });
    state.map.addLayer({ id: sourceId, type: 'raster', source: sourceId }, aboveBasemap(state));
    const entry = { key, sourceId, layerIds: [sourceId] };
    _packTiles.set(key, { state, reader, kind: 'raster', entry });
    state.packLayers.set(layerId, entry);
}

function addPackVectorLayers(pack, sourceLayer) {
    const { state, entry } = pack;
    if (_maps[state.divId] !== state || entry.sourceLayers.has(sourceLayer) || !state.map.getSource(entry.sourceId)) return;
    entry.sourceLayers.add(sourceLayer);
    packVectorLayers(entry.sourceId + '-', entry.sourceId, sourceLayer, entry.sourceLayers.size - 1, entry.colors).forEach(layer => {
        state.map.addLayer(layer, firstOverlayLayer());
        entry.layerIds.push(layer.id);
    });
}

/** A tile's MVT layers the pack's metadata didn't name are drawn too, once the tile is read. */
function discoverPackLayers(pack, data) {
    const unknown = mvtLayerNames(new Uint8Array(data)).filter(name => !pack.entry.sourceLayers.has(name));
    if (unknown.length) setTimeout(() => unknown.forEach(name => addPackVectorLayers(pack, name)), 0);
}

/** options = { minZoom, maxZoom, bounds: [west, south, east, north] | null, sourceLayers: [name…], colors } — the pack's. */
function addLocalVectorLayer(divId, layerId, reader, options) {
    const state = _maps[divId];
    if (!state || state.packLayers.has(layerId)) return;
    const key = 'p' + (++_packSequence);
    const sourceId = PACK_PREFIX + key;
    state.map.addSource(sourceId, {
        type: 'vector', tiles: [packTileTemplate(key)],
        minzoom: packZoom(options?.minZoom, 0), maxzoom: packZoom(options?.maxZoom, 14), bounds: packBounds(options?.bounds)
    });
    const entry = { key, sourceId, layerIds: [], sourceLayers: new Set(), colors: options?.colors || null };
    const pack = { state, reader, kind: 'vector', entry };
    _packTiles.set(key, pack);
    state.packLayers.set(layerId, entry);
    (Array.isArray(options?.sourceLayers) ? options.sourceLayers : []).forEach(name => addPackVectorLayers(pack, String(name)));
    // MapLibre reads no tile of a source no layer draws: a pack whose metadata names no layer gets an invisible one, so
    // its tiles are read and their layers found (discoverPackLayers).
    if (entry.layerIds.length === 0) {
        const probe = { id: sourceId + '-probe', type: 'circle', source: sourceId, 'source-layer': sourceId + '-probe', paint: { 'circle-radius': 0 } };
        state.map.addLayer(probe, firstOverlayLayer());
        entry.layerIds.push(probe.id);
    }
}

function removePackEntry(state, entry) {
    _packTiles.delete(entry.key);
    entry.layerIds.forEach(id => { if (state.map.getLayer(id)) state.map.removeLayer(id); });
    if (state.map.getSource(entry.sourceId)) state.map.removeSource(entry.sourceId);
}

function removeLocalLayer(divId, layerId) {
    const state = _maps[divId];
    const entry = state?.packLayers.get(layerId);
    if (!entry) return;
    state.packLayers.delete(layerId);
    removePackEntry(state, entry);
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
    if (!bounds) {
        // A kernel feature off screen is only a tile elsewhere (XD01-436): its overlay, drawn on selection, brings it.
        state.pendingPan = { id, until: Date.now() + PENDING_OVERLAY_MS };
        return;
    }
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
    showTileLayers,
    hideTileLayers,
    setHiddenTileFeatures,
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
    addLocalRasterLayer,
    removeLocalRasterLayer: removeLocalLayer,
    addLocalVectorLayer,
    removeLocalVectorLayer: removeLocalLayer,
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
