// Pure helpers of geoassets.maplibre.js (XD01-435). Node's own test runner, no npm dependency:
//   node --test tests/js/geoassets.maplibre.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    planStyleUpdate, tileVersionOf, tokenScopeOf, isUnder, stateGroups, stateExpression, dashArrayOf, effectiveStyle,
    toSourceFeature, wmsTileUrl, expandSubdomains, boundsOf, nearestVertex, featureIdOf, roundGeometry, toMapLibreZoom,
    fromMapLibreZoom, LIBRARIES
} from '../../apps/GeoAssets.Shared/wwwroot/js/geoassets.maplibre.js';

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
