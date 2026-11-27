// Pure helpers of geoassets.maplibre.js (XD01-435). Node's own test runner, no npm dependency:
//   node --test tests/js/geoassets.maplibre.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    planStyleUpdate, tileVersionOf, tokenScopeOf, isUnder, stateGroups, stateExpression, dashArrayOf, effectiveStyle,
    toSourceFeature, wmsTileUrl, expandSubdomains, boundsOf, nearestVertex, featureIdOf, roundGeometry, toMapLibreZoom,
    fromMapLibreZoom, LIBRARIES, rasterTierOf, tileStyleInfo, withoutHiddenIds, tileBoundsOf, tileVertices, isTileSource,
    LOCAL_LIBRARIES, librariesFor, PACK_PROTOCOL, packTileTemplate, parsePackTileUrl, mvtLayerNames, packVectorLayers,
    toArrayBuffer, isRepeatedContextMenu
} from '../../apps/GeoAssets.Shared/wwwroot/js/geoassets.maplibre.js';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';

const style = (v, layers = [{ id: 't-point', type: 'circle', source: 'geoassets', 'source-layer': 't' }], raster = false) => ({
    version: 8,
    metadata: { 'geoassets:version': v },
    sources: Object.assign(
        { geoassets: { type: 'vector', tiles: [`https://gw/tiles/assets/${v}/{z}/{x}/{y}.mvt`], maxzoom: 18, promoteId: 'id' } },
        raster ? { 'geoassets-raster': { type: 'raster', tiles: [`https://gw/tiles/assets/${v}/{z}/{x}/{y}.png`], tileSize: 256 } } : {}),
    layers
});

test('the first style rebuilds', () => {
    assert.deepEqual(planStyleUpdate(null, style('a-1-0.1')), { kind: 'rebuild', tiles: [] });
});

test('the same style changes nothing', () => {
    assert.deepEqual(planStyleUpdate(style('a-1-0.1'), style('a-1-0.1')), { kind: 'none', tiles: [] });
});

test('a new {v} with the same layers only re-points the tile URLs of every source', () => {
    const plan = planStyleUpdate(style('a-1-0.1', undefined, true), style('a-1-0.2', undefined, true));
    assert.equal(plan.kind, 'tiles');
    assert.deepEqual(plan.tiles, [
        { sourceId: 'geoassets', tiles: ['https://gw/tiles/assets/a-1-0.2/{z}/{x}/{y}.mvt'] },
        { sourceId: 'geoassets-raster', tiles: ['https://gw/tiles/assets/a-1-0.2/{z}/{x}/{y}.png'] }
    ]);
});

test('changed layers, a source added or a source property changed rebuild', () => {
    assert.equal(planStyleUpdate(style('a-1-0.1'), style('b-1-0.1', [{ id: 'other', type: 'fill', source: 'geoassets' }])).kind, 'rebuild');
    assert.equal(planStyleUpdate(style('a-1-0.1'), style('a-1-0.2', undefined, true)).kind, 'rebuild');
    const next = style('a-1-0.2');
    next.sources.geoassets.maxzoom = 20;
    assert.equal(planStyleUpdate(style('a-1-0.1'), next).kind, 'rebuild');
});

test('no next style changes nothing', () => {
    assert.deepEqual(planStyleUpdate(style('a'), null), { kind: 'none', tiles: [] });
});

test('the version is the metadata entry, else null', () => {
    assert.equal(tileVersionOf(style('a-1-0.7')), 'a-1-0.7');
    assert.equal(tileVersionOf({}), null);
    assert.equal(tileVersionOf({ metadata: { 'geoassets:version': '' } }), null);
    assert.equal(tileVersionOf(null), null);
});

test('the token goes under the style directory of the same origin only', () => {
    const scope = tokenScopeOf('https://gw.example/prefix/tiles/style.json');
    assert.equal(scope, 'https://gw.example/prefix/tiles/');
    assert.ok(isUnder('https://gw.example/prefix/tiles/assets/v/1/2/3.mvt', scope));
    assert.ok(!isUnder('https://gw.example/prefix/api/features', scope));
    assert.ok(!isUnder('https://evil.example/prefix/tiles/assets/v/1/2/3.mvt', scope));
    assert.ok(!isUnder('http://gw.example/prefix/tiles/x.mvt', scope));
    assert.ok(!isUnder('not a url', scope));
    assert.ok(!isUnder('https://gw.example/prefix/tiles/x', null));
    assert.equal(tokenScopeOf('not a url'), null);
});

test('state groups: the highlight wins and an id is in one group only', () => {
    const groups = stateGroups(new Map([['a', 'selected'], ['b', 'selected'], ['c', 'disabled']]), new Set(['b']));
    assert.deepEqual(groups, [{ name: 'selected', ids: ['a'] }, { name: 'highlighted', ids: ['b'] }, { name: 'disabled', ids: ['c'] }]);
    assert.deepEqual(stateGroups(new Map(), new Set()), []);
});

test('the state expression matches the ids and keeps the base elsewhere', () => {
    const groups = [{ name: 'selected', ids: ['a'] }, { name: 'disabled', ids: ['c'] }];
    const value = name => (name === 'selected' ? '#89b4fa' : null);
    assert.deepEqual(stateExpression('#ff0000', groups, value),
        ['match', ['to-string', ['id']], ['a'], '#89b4fa', ['c'], '#ff0000', '#ff0000']);
    assert.equal(stateExpression('#ff0000', [], value), '#ff0000');
    const zoomed = ['interpolate', ['linear'], ['zoom'], 5, 1, 10, 3];
    assert.equal(stateExpression(zoomed, groups, value), zoomed);
});

test('dash arrays are converted to line widths', () => {
    assert.deepEqual(dashArrayOf('8 6', 2), [4, 3]);
    assert.deepEqual(dashArrayOf('4,3', 0), [4, 3]);
    assert.equal(dashArrayOf(null, 2), null);
    assert.equal(dashArrayOf('5', 2), null);
});

test('the effective style is the resolved one, else the type colour', () => {
    const f = { properties: { assetTypeId: 't' } };
    assert.equal(effectiveStyle(f, { t: '#123456' }, null).color, '#123456');
    assert.equal(effectiveStyle(f, null, null).color, '#3388ff');
    assert.equal(effectiveStyle(f, null, { color: '#abcdef', weight: 2, dashArray: '' }).dashArray, null);
});

test('a source feature carries its promoted id, type, name and style', () => {
    const f = toSourceFeature(
        { type: 'Feature', id: 42, geometry: { type: 'Point', coordinates: [1, 2] }, properties: { assetTypeId: 't', name: 'Pole', customAttributes: { a: 1 } } },
        { color: '#111111', weight: 2, radius: 6, fillColor: '#222222', fillOpacity: 0.5, dashArray: '4 2' });
    assert.deepEqual(f, {
        type: 'Feature', id: '42', geometry: { type: 'Point', coordinates: [1, 2] },
        properties: { id: '42', assetTypeId: 't', name: 'Pole', __color: '#111111', __weight: 2, __radius: 6, __fillColor: '#222222', __fillOpacity: 0.5, __dash: [2, 1] }
    });
    assert.equal(toSourceFeature({ id: 'x', geometry: null }, {}).properties.assetTypeId, 'default');
});

test('WMS tiles are asked on the EPSG:3857 grid', () => {
    const url = wmsTileUrl('https://ows.example/wms', { layers: 'roads', version: '1.1.1' });
    assert.ok(url.startsWith('https://ows.example/wms?SERVICE=WMS&REQUEST=GetMap&VERSION=1.1.1&LAYERS=roads'));
    assert.ok(url.includes('SRS=EPSG%3A3857'));
    assert.ok(url.endsWith('&BBOX={bbox-epsg-3857}'));
    assert.ok(wmsTileUrl('https://ows.example/wms?map=x', { version: '1.3.0' }).includes('?map=x&SERVICE=WMS') );
    assert.ok(wmsTileUrl('https://ows.example/wms', { version: '1.3.0' }).includes('CRS=EPSG%3A3857'));
});

test('subdomains are expanded', () => {
    assert.deepEqual(expandSubdomains('https://{s}.t/{z}/{x}/{y}.png'), ['https://a.t/{z}/{x}/{y}.png', 'https://b.t/{z}/{x}/{y}.png', 'https://c.t/{z}/{x}/{y}.png']);
    assert.deepEqual(expandSubdomains('https://t/{z}/{x}/{y}.png'), ['https://t/{z}/{x}/{y}.png']);
});

test('bounds of every geometry kind', () => {
    assert.deepEqual(boundsOf({ type: 'Point', coordinates: [1, 2] }), [1, 2, 1, 2]);
    assert.deepEqual(boundsOf({ type: 'Polygon', coordinates: [[[0, 0], [2, 0], [2, 3], [0, 0]]] }), [0, 0, 2, 3]);
    assert.deepEqual(boundsOf({ type: 'GeometryCollection', geometries: [{ type: 'Point', coordinates: [5, 5] }, { type: 'LineString', coordinates: [[-1, 0], [1, 1]] }] }), [-1, 0, 5, 5]);
    assert.equal(boundsOf(null), null);
});

test('the nearest vertex within the distance, in the snap scope', () => {
    const project = ([x, y]) => ({ x: x * 10, y: y * 10 });
    const features = [
        { properties: { assetTypeId: 'a' }, geometry: { type: 'LineString', coordinates: [[1, 1], [3, 3]] } },
        { properties: { assetTypeId: 'b' }, geometry: { type: 'Point', coordinates: [1.2, 1] } }
    ];
    assert.deepEqual(nearestVertex({ x: 11, y: 10 }, features, project, 15), [1.2, 1]);
    assert.deepEqual(nearestVertex({ x: 11, y: 10 }, features, project, 15, f => f.properties.assetTypeId === 'a'), [1, 1]);
    assert.equal(nearestVertex({ x: 100, y: 100 }, features, project, 15), undefined);
});

test('the feature id is the promoted property, else the feature id', () => {
    assert.equal(featureIdOf({ id: 7, properties: { id: 'pole-1' } }), 'pole-1');
    assert.equal(featureIdOf({ id: 7, properties: {} }), '7');
    assert.equal(featureIdOf({ properties: { id: '' } }), null);
    assert.equal(featureIdOf(null), null);
});

test('geometries are rounded to terra-draw\'s precision', () => {
    assert.deepEqual(roundGeometry({ type: 'Point', coordinates: [1.1234567891234, 2] }), { type: 'Point', coordinates: [1.123456789, 2] });
    assert.deepEqual(roundGeometry({ type: 'Polygon', coordinates: [[[0.0000000001, 1]]] }), { type: 'Polygon', coordinates: [[[0, 1]]] });
});

test('zooms cross the contract in the 256 px scale', () => {
    assert.equal(toMapLibreZoom(15), 14);
    assert.equal(fromMapLibreZoom(14), 15);
});

test('every CDN library is an exact version with SRI', () => {
    for (const lib of Object.values(LIBRARIES)) {
        const url = lib.src || lib.href;
        assert.match(url, /^https:\/\/unpkg\.com\/[a-z-]+@\d+\.\d+\.\d+\//);
        assert.match(lib.integrity, /^sha384-[A-Za-z0-9+/=]{64}$/);
    }
});

// ─── XD01-436: the hybrid LOD renderer's helpers ─────────────────────────────────────────────────────────────────────

const rasterLayer = (minzoom, maxzoom) => Object.assign({ id: 'geoassets-raster', type: 'raster', source: 'geoassets-raster' },
    minzoom === undefined ? {} : { minzoom }, maxzoom === undefined ? {} : { maxzoom });

test('the raster tier is the raster layer\'s zooms, maxzoom exclusive', () => {
    assert.deepEqual(rasterTierOf(style('v', [rasterLayer(0, 14)], true)), { minZoom: 0, maxZoom: 13 });
    assert.deepEqual(rasterTierOf(style('v', [rasterLayer(4, 12)], true)), { minZoom: 4, maxZoom: 11 });
    assert.deepEqual(rasterTierOf(style('v', [rasterLayer(10)], true)), { minZoom: 10, maxZoom: 24 }, 'no maxzoom = up to the top');
});

test('no raster layer, or no raster source, is no raster tier (the default LOD policy)', () => {
    assert.equal(rasterTierOf(style('v')), null);
    assert.equal(rasterTierOf(style('v', [rasterLayer(0, 14)], false)), null);
    assert.equal(rasterTierOf(style('v', [rasterLayer(5, 5)], true)), null, 'an empty run');
    assert.equal(rasterTierOf(null), null);
});

test('the style info carries {v}, the raster tier and the rounded zoom', () => {
    assert.deepEqual(tileStyleInfo(style('a-1-0.2', [rasterLayer(0, 14)], true), 12.6),
        { version: 'a-1-0.2', rasterMinZoom: 0, rasterMaxZoom: 13, zoom: 13 });
    assert.deepEqual(tileStyleInfo(style('a-1-0.2'), 15),
        { version: 'a-1-0.2', rasterMinZoom: null, rasterMaxZoom: null, zoom: 15 });
});

test('hidden ids are filtered out of a vector tile layer, keeping its own filter', () => {
    const own = ['match', ['geometry-type'], ['Point', 'MultiPoint'], true, false];
    assert.equal(withoutHiddenIds(own, []), own);
    assert.equal(withoutHiddenIds(undefined, []), null);
    assert.deepEqual(withoutHiddenIds(own, ['a', 7]),
        ['all', own, ['!', ['in', ['to-string', ['get', 'id']], ['literal', ['a', '7']]]]]);
    assert.deepEqual(withoutHiddenIds(null, ['a']), ['!', ['in', ['to-string', ['get', 'id']], ['literal', ['a']]]]);
});

test('tile bounds in degrees', () => {
    assert.deepEqual(tileBoundsOf(0, 0, 0).map(n => +n.toFixed(6)), [-180, -85.051129, 180, 85.051129]);
    const [w, s, e, n] = tileBoundsOf(1, 1, 0);
    assert.deepEqual([w, e], [0, 180]);
    assert.ok(s === 0 && n > 85);
});

test('a tile feature\'s snap vertices are the ones inside its own tile (not its clipped ends)', () => {
    // Tile 1/1/0 is the north-east quarter: [0, 0] … [180, 85]. A line clipped at the buffer: its west end is past x = 0.
    const clipped = { _z: 1, _x: 1, _y: 0, geometry: { type: 'LineString', coordinates: [[-0.5, 10], [10, 10], [20, 20]] } };
    assert.deepEqual(tileVertices(clipped), [[10, 10], [20, 20]]);
    const onTheEdge = { _z: 1, _x: 1, _y: 0, geometry: { type: 'Point', coordinates: [0, 10] } };
    assert.deepEqual(tileVertices(onTheEdge), [[0, 10]]);
    const noTile = { geometry: { type: 'LineString', coordinates: [[-0.5, 10], [10, 10]] } };
    assert.deepEqual(tileVertices(noTile), [[-0.5, 10], [10, 10]], 'a feature without its tile keeps every position');
});

test('the tile style\'s sources are the kernel\'s own and its keyed ones (XD01-459), never the features\' overlay', () => {
    assert.equal(isTileSource('geoassets'), true);
    assert.equal(isTileSource('geoassets-raster'), true);
    assert.equal(isTileSource('geoassets-source-mvt-basemap'), true);
    assert.equal(isTileSource('geoassets-source-png-ortho'), true);
    assert.equal(isTileSource('geoassets-features'), false);
    assert.equal(isTileSource('osm'), false);
    assert.equal(isTileSource(undefined), false);
});

// ─── XD01-460: MAUI's bundled libraries and offline map packs ────────────────────────────────────────────────────────

const MAUI_LIBRARIES = new URL('../../apps/GeoAssets.MAUI/wwwroot/lib/maplibre/', import.meta.url);

test('the libraries MAUI bundles are the pinned files, byte for byte (their SRI)', () => {
    for (const lib of Object.values(LOCAL_LIBRARIES)) {
        const bytes = readFileSync(new URL(lib.path, MAUI_LIBRARIES));
        assert.equal('sha384-' + createHash('sha384').update(bytes).digest('base64'), lib.integrity, lib.path);
        assert.match(lib.path, /^[a-z-]+@\d+\.\d+\.\d+\/dist\//);
    }
    assert.equal(LOCAL_LIBRARIES.maplibreCss.integrity, LIBRARIES.maplibreCss.integrity);
    assert.equal(LOCAL_LIBRARIES.terraDraw.integrity, LIBRARIES.terraDraw.integrity);
    assert.equal(LOCAL_LIBRARIES.terraDrawAdapter.integrity, LIBRARIES.terraDrawAdapter.integrity);
    assert.ok(LIBRARIES.maplibre.src.includes('maplibre-gl@5.24.0/') && LOCAL_LIBRARIES.maplibre.path.startsWith('maplibre-gl@5.24.0/'),
        'the same MapLibre version as the CDN\'s');
});

test('without a library base the CDN is used, as on the Web', () => {
    assert.equal(librariesFor(null, 'https://0.0.0.1/'), LIBRARIES);
    assert.equal(librariesFor('  ', 'https://0.0.0.1/'), LIBRARIES);
    assert.equal(librariesFor(undefined, 'https://0.0.0.1/'), LIBRARIES);
});

test('a relative library base is the page\'s own origin — no script origin in the CSP', () => {
    for (const origin of ['https://0.0.0.1/', 'app://0.0.0.1/']) {
        const libs = librariesFor('lib/maplibre', origin);
        assert.equal(libs.maplibre.src, new URL('lib/maplibre/maplibre-gl@5.24.0/dist/maplibre-gl-csp.js', origin).href);
        assert.equal(libs.maplibreWorker.src, new URL('lib/maplibre/maplibre-gl@5.24.0/dist/maplibre-gl-csp-worker.js', origin).href);
        assert.equal(libs.maplibreCss.href, new URL('lib/maplibre/maplibre-gl@5.24.0/dist/maplibre-gl.css', origin).href);
        assert.equal(libs.terraDraw.src, new URL('lib/maplibre/terra-draw@1.37.0/dist/terra-draw.umd.js', origin).href);
        for (const lib of Object.values(libs)) assert.equal(new URL(lib.src || lib.href).origin, new URL(origin).origin);
        assert.equal(libs.maplibre.global, 'maplibregl');
        assert.equal(libs.terraDrawAdapter.global, 'terraDrawMaplibreGlAdapter');
        assert.equal(libs.maplibre.integrity, LOCAL_LIBRARIES.maplibre.integrity);
    }
    assert.equal(librariesFor('lib/maplibre/', 'https://0.0.0.1/').maplibre.src, librariesFor('lib/maplibre', 'https://0.0.0.1/').maplibre.src);
});

test('a pack tile URL round-trips its key and tile, and nothing else parses', () => {
    const template = packTileTemplate('p 7/ä');
    assert.ok(template.startsWith(PACK_PROTOCOL + '://'));
    const url = template.replace('{z}', '12').replace('{x}', '2045').replace('{y}', '1500');
    assert.deepEqual(parsePackTileUrl(url), { key: 'p 7/ä', z: 12, x: 2045, y: 1500 });
    assert.deepEqual(parsePackTileUrl('geoassets-pack://p1/0/0/0'), { key: 'p1', z: 0, x: 0, y: 0 });
    for (const bad of [
        'geoassets-pack://p1/1/2/0', 'geoassets-pack://p1/1/0/2', 'geoassets-pack://p1/25/0/0', 'geoassets-pack://p1/a/0/0',
        'geoassets-pack://p1/1/0', 'geoassets-pack://p1/1/0/0?x', 'https://gw/tiles/1/0/0', 'geoassets-pack://%E0%A4%A/1/0/0',
        'geoassets-pack:///1/0/0', null, undefined
    ]) assert.equal(parsePackTileUrl(bad), null, String(bad));
});

// A hand-encoded MVT: layers "poles" (one feature, extent), "lines", "poles" again.
const varint = n => { const out = []; do { let b = n & 0x7f; n >>>= 7; if (n) b |= 0x80; out.push(b); } while (n); return out; };
const field = (number, bytes) => [...varint((number << 3) | 2), ...varint(bytes.length), ...bytes];
const text = s => [...new TextEncoder().encode(s)];
const mvtLayer = (name, extra = []) => field(3, [...varint((15 << 3) | 0), 2, ...field(1, text(name)), ...extra]);

test('the layer names of a vector tile are read from its protobuf, each once', () => {
    const feature = field(2, [...varint((1 << 3) | 0), 7, ...varint((3 << 3) | 0), 1]);
    const tile = new Uint8Array([...mvtLayer('poles', [...feature, ...varint((5 << 3) | 0), ...varint(4096)]), ...mvtLayer('Áreas'), ...mvtLayer('poles')]);
    assert.deepEqual(mvtLayerNames(tile), ['poles', 'Áreas']);
    assert.deepEqual(mvtLayerNames(tile.buffer), ['poles', 'Áreas']);
    assert.deepEqual(mvtLayerNames(new Uint8Array(0)), []);
    assert.deepEqual(mvtLayerNames(null), []);
    assert.deepEqual(mvtLayerNames(tile.subarray(0, tile.length - 3)), ['poles', 'Áreas'], 'a truncated tile answers what was read');
    assert.deepEqual(mvtLayerNames(new Uint8Array([0xff, 0xff])), []);
});

test('a pack\'s vector layers draw each geometry of one MVT layer in the pack colours', () => {
    const layers = packVectorLayers('geoassets-pack-p1-', 'geoassets-pack-p1', 'poles', 0, { point: '#ff0000' });
    assert.deepEqual(layers.map(l => [l.id, l.type]), [
        ['geoassets-pack-p1-0-fill', 'fill'], ['geoassets-pack-p1-0-outline', 'line'],
        ['geoassets-pack-p1-0-line', 'line'], ['geoassets-pack-p1-0-point', 'circle']]);
    for (const l of layers) {
        assert.equal(l.source, 'geoassets-pack-p1');
        assert.equal(l['source-layer'], 'poles');
    }
    assert.equal(layers[3].paint['circle-color'], '#ff0000');
    assert.equal(layers[2].paint['line-color'], '#3498db', 'a colour not given is the default line type\'s');
    assert.equal(layers[0].paint['fill-color'], '#2ecc71');
    assert.deepEqual(layers[3].filter, ['match', ['geometry-type'], ['Point', 'MultiPoint'], true, false]);
});

test('tile bytes from .NET become an ArrayBuffer of their own; none is null', () => {
    const backing = new Uint8Array([9, 1, 2, 3, 9]);
    const view = backing.subarray(1, 4);
    assert.deepEqual([...new Uint8Array(toArrayBuffer(view))], [1, 2, 3]);
    assert.deepEqual([...new Uint8Array(toArrayBuffer([4, 5]))], [4, 5]);
    const own = new Uint8Array([6]).buffer;
    assert.equal(toArrayBuffer(own), own);
    for (const none of [null, undefined, new Uint8Array(0), new ArrayBuffer(0), [], 'AQI=']) assert.equal(toArrayBuffer(none), null);
});

test('a context menu within 800 ms of the last one is the same gesture', () => {
    assert.equal(isRepeatedContextMenu(null, 1000), false);
    assert.equal(isRepeatedContextMenu(1000, 1500), true);
    assert.equal(isRepeatedContextMenu(1000, 1800), false);
    assert.equal(isRepeatedContextMenu(2000, 1000), false);
});
