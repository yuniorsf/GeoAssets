/**
 * GeoAssets — Unified JavaScript Interop
 * Provides Leaflet map management, draw tools (Leaflet-Geoman),
 * and browser storage utilities for the Blazor app.
 *
 * Render modes (configured via MapInterop:RenderMode in appsettings.json):
 *   leaflet — default SVG rendering via L.geoJSON (best compatibility)
 *   canvas  — Leaflet's built-in Canvas renderer (better DOM performance)
 *   webgl   — Custom WebGL overlay; events via invisible SVG layers (highest throughput)
 */
window.GeoAssets = (function () {
    'use strict';

    // ─── State registry ──────────────────────────────────────────────────────
    // divId → { map, featureLayers, layerGroups, tileLayers, dotNetRef,
    //           renderMode, canvasRenderer?, webgl? }
    const _maps = {};

    function getState(divId) { return _maps[divId] || null; }

    // ─── WebGL shader sources ────────────────────────────────────────────────

    const _VERT_SRC = `
        attribute vec2 a_pos;
        uniform vec2 u_res;
        uniform float u_ptSize;
        void main() {
            vec2 clip = ((a_pos / u_res) * 2.0 - 1.0) * vec2(1.0, -1.0);
            gl_Position = vec4(clip, 0.0, 1.0);
            gl_PointSize = u_ptSize;
        }`;

    const _FRAG_SRC = `
        precision mediump float;
        uniform vec4 u_color;
        void main() { gl_FragColor = u_color; }`;

    // ─── WebGL helpers ────────────────────────────────────────────────────────

    function _compileShader(gl, type, src) {
        const s = gl.createShader(type);
        gl.shaderSource(s, src);
        gl.compileShader(s);
        if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) {
            console.error('GeoAssets WebGL shader:', gl.getShaderInfoLog(s));
            gl.deleteShader(s);
            return null;
        }
        return s;
    }

    function _createProgram(gl) {
        const vert = _compileShader(gl, gl.VERTEX_SHADER,   _VERT_SRC);
        const frag = _compileShader(gl, gl.FRAGMENT_SHADER, _FRAG_SRC);
        if (!vert || !frag) return null;
        const prog = gl.createProgram();
        gl.attachShader(prog, vert);
        gl.attachShader(prog, frag);
        gl.linkProgram(prog);
        if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
            console.error('GeoAssets WebGL link:', gl.getProgramInfoLog(prog));
            return null;
        }
        return prog;
    }

    function _hexToVec4(hex, alpha) {
        hex = hex.replace('#', '');
        if (hex.length === 3) hex = hex.split('').map(c => c + c).join('');
        return [
            parseInt(hex.substring(0, 2), 16) / 255,
            parseInt(hex.substring(2, 4), 16) / 255,
            parseInt(hex.substring(4, 6), 16) / 255,
            alpha
        ];
    }

    /**
     * Ear-clipping triangulation for a simple polygon ring (no holes).
     * Returns a Float32Array of flat triangle vertices [x0,y0, x1,y1, x2,y2, …].
     */
    function _earcut(ring) {
        const n = ring.length;
        if (n < 3) return new Float32Array(0);

        // Compute signed area; reverse ring if CW so we always work CCW
        let area = 0;
        for (let i = 0; i < n; i++) {
            const [x1, y1] = ring[i], [x2, y2] = ring[(i + 1) % n];
            area += x1 * y2 - x2 * y1;
        }
        const pts = area > 0 ? [...ring] : [...ring].reverse();

        function cross(o, a, b) {
            return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
        }
        function ptInTri(ax, ay, bx, by, cx, cy, px, py) {
            const d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            const d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            const d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
        }

        const idx = Array.from({ length: pts.length }, (_, i) => i);
        const verts = [];
        let i = 0, guard = 0;

        while (idx.length > 3 && guard < idx.length * 3) {
            const len = idx.length;
            const pi = (i - 1 + len) % len, ci = i, ni = (i + 1) % len;
            const [ax, ay] = pts[idx[pi]], [bx, by] = pts[idx[ci]], [cx, cy] = pts[idx[ni]];
            let ear = cross([ax, ay], [bx, by], [cx, cy]) > 0;
            if (ear) {
                for (let j = 0; j < len; j++) {
                    if (j === pi || j === ci || j === ni) continue;
                    const [px, py] = pts[idx[j]];
                    if (ptInTri(ax, ay, bx, by, cx, cy, px, py)) { ear = false; break; }
                }
            }
            if (ear) {
                verts.push(ax, ay, bx, by, cx, cy);
                idx.splice(ci, 1);
                if (i >= idx.length) i = 0;
                guard = 0;
            } else {
                i = (i + 1) % idx.length;
                guard++;
            }
        }
        if (idx.length === 3) {
            const [ax, ay] = pts[idx[0]], [bx, by] = pts[idx[1]], [cx, cy] = pts[idx[2]];
            verts.push(ax, ay, bx, by, cx, cy);
        }
        return new Float32Array(verts);
    }

    function _projectRing(map, coords) {
        return coords.map(([lng, lat]) => {
            const p = map.latLngToContainerPoint(L.latLng(lat, lng));
            return [p.x, p.y];
        });
    }

    /**
     * Schedules a WebGL redraw on the next animation frame.
     *
     * Double-buffer pattern:
     *   • The browser already maintains a front/back GPU swap-chain; all GL
     *     commands go to the back buffer and are presented atomically at the
     *     next vsync after requestAnimationFrame completes — the user never
     *     sees a half-drawn frame.
     *   • The dirty flag ensures at most one RAF callback is in flight at any
     *     time, so multiple synchronous calls (e.g. from rapid event bursts)
     *     collapse into a single draw per animation frame.
     */
    function _scheduleRedraw(divId) {
        const wgl = _maps[divId]?.webgl;
        if (!wgl || wgl.rafHandle !== null) return; // already queued
        wgl.rafHandle = requestAnimationFrame(() => {
            wgl.rafHandle = null;
            _webglRedraw(divId);
        });
    }

    function _initWebGL(divId) {
        const state = _maps[divId];
        const container = state.map.getContainer();

        const canvas = document.createElement('canvas');
        canvas.style.cssText =
            'position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:400;';
        canvas.width  = container.clientWidth;
        canvas.height = container.clientHeight;
        container.appendChild(canvas);

        const gl = canvas.getContext('webgl', { alpha: true, antialias: true });
        if (!gl) {
            console.error('GeoAssets: WebGL unavailable — falling back is not automatic; reload with a different RenderMode.');
            canvas.remove();
            return;
        }

        gl.clearColor(0, 0, 0, 0);
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

        const prog = _createProgram(gl);
        if (!prog) { canvas.remove(); return; }

        gl.useProgram(prog);
        state.webgl = {
            canvas,
            gl,
            prog,
            locs: {
                pos:    gl.getAttribLocation(prog,  'a_pos'),
                res:    gl.getUniformLocation(prog, 'u_res'),
                ptSize: gl.getUniformLocation(prog, 'u_ptSize'),
                color:  gl.getUniformLocation(prog, 'u_color')
            },
            buf:          gl.createBuffer(),
            features:     new Map(),   // featureId → { feature, colorHex, assetTypeId }
            hiddenTypes:  new Set(),
            rafHandle:    null,        // pending requestAnimationFrame id
            anchorLatLng: null,        // map center at last full draw
            anchorPx:     null         // pixel position of anchorLatLng at last full draw
        };

        // ── Pan: CSS translate (zero GPU cost — compositor handles it) ────────
        // While the user drags the map we simply shift the WebGL canvas by the
        // pixel delta from where it was last fully rendered.  This gives smooth
        // 60 fps panning without any WebGL work at all; the browser uses its own
        // compositor to blend the translated overlay over the tile layer.
        state.map.on('move', () => {
            const wgl = state.webgl;
            if (!wgl?.anchorLatLng) return;
            const cur = state.map.latLngToContainerPoint(wgl.anchorLatLng);
            const dx  = cur.x - wgl.anchorPx.x;
            const dy  = cur.y - wgl.anchorPx.y;
            wgl.canvas.style.transform = `translate(${dx}px,${dy}px)`;
        });

        // ── Settle: reset transform and schedule a true redraw via RAF ────────
        state.map.on('moveend zoomend resize', () => {
            const wgl = state.webgl;
            if (!wgl) return;
            wgl.canvas.style.transform = '';
            const cw = container.clientWidth, ch = container.clientHeight;
            if (wgl.canvas.width !== cw || wgl.canvas.height !== ch) {
                wgl.canvas.width = cw; wgl.canvas.height = ch;
                gl.viewport(0, 0, cw, ch);
            }
            _scheduleRedraw(divId);
        });
    }

    function _webglRedraw(divId) {
        const state = _maps[divId];
        const wgl   = state?.webgl;
        if (!wgl) return;
        const { canvas, gl, prog, locs, features, hiddenTypes } = wgl;
        gl.viewport(0, 0, canvas.width, canvas.height);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.useProgram(prog);
        gl.uniform2f(locs.res, canvas.width, canvas.height);
        features.forEach(({ feature, colorHex, assetTypeId }) => {
            if (!hiddenTypes.has(assetTypeId))
                _webglDrawGeoFeature(state, feature, colorHex);
        });
        // Record the anchor for the pan-offset optimisation
        wgl.anchorLatLng = state.map.getCenter();
        wgl.anchorPx     = state.map.latLngToContainerPoint(wgl.anchorLatLng);
    }

    function _webglDrawGeoFeature(state, feature, hex) {
        const { type, coordinates: c } = feature.geometry || {};
        if (!type) return;
        if      (type === 'Point')           _wglPoint(state, c, hex);
        else if (type === 'MultiPoint')      c.forEach(p  => _wglPoint(state, p, hex));
        else if (type === 'LineString')      _wglLine(state, c, hex);
        else if (type === 'MultiLineString') c.forEach(l  => _wglLine(state, l, hex));
        else if (type === 'Polygon')         _wglPolygon(state, c, hex);
        else if (type === 'MultiPolygon')    c.forEach(r  => _wglPolygon(state, r, hex));
    }

    function _wglDraw(state, data, mode, rgba) {
        const { gl, locs, buf } = state.webgl;
        gl.bindBuffer(gl.ARRAY_BUFFER, buf);
        gl.bufferData(gl.ARRAY_BUFFER, data, gl.DYNAMIC_DRAW);
        gl.enableVertexAttribArray(locs.pos);
        gl.vertexAttribPointer(locs.pos, 2, gl.FLOAT, false, 0, 0);
        gl.uniform4fv(locs.color, rgba);
        gl.drawArrays(mode, 0, data.length / 2);
    }

    function _wglPoint(state, coord, hex) {
        const { gl, locs } = state.webgl;
        const p = state.map.latLngToContainerPoint(L.latLng(coord[1], coord[0]));
        gl.uniform1f(locs.ptSize, 10);
        _wglDraw(state, new Float32Array([p.x, p.y]), gl.POINTS, _hexToVec4(hex, 0.9));
    }

    function _wglLine(state, coords, hex) {
        const { gl } = state.webgl;
        const flat = _projectRing(state.map, coords).flat();
        _wglDraw(state, new Float32Array(flat), gl.LINE_STRIP, _hexToVec4(hex, 0.9));
    }

    function _wglPolygon(state, rings, hex) {
        const { gl } = state.webgl;
        const exterior = _projectRing(state.map, rings[0]);

        // Filled triangulation of exterior ring
        const fill = _earcut(exterior);
        if (fill.length > 0)
            _wglDraw(state, fill, gl.TRIANGLES, _hexToVec4(hex, 0.35));

        // Outline for all rings (including holes)
        for (const ring of rings) {
            const proj  = _projectRing(state.map, ring);
            const close = [...proj.flat(), proj[0][0], proj[0][1]];
            _wglDraw(state, new Float32Array(close), gl.LINE_STRIP, _hexToVec4(hex, 0.9));
        }
    }

    // ─── Map Lifecycle ───────────────────────────────────────────────────────

    function initializeMap(divId, lat, lon, zoom, renderMode) {
        if (_maps[divId]) return; // idempotent

        const mode = (renderMode || 'leaflet').toLowerCase();
        const map  = L.map(divId, { zoomControl: true }).setView([lat, lon], zoom);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            maxZoom: 19
        }).addTo(map);

        _maps[divId] = {
            map,
            featureLayers:  new Map(),  // featureId → L.Layer (visual or event layer)
            layerGroups:    new Map(),  // assetTypeId → L.LayerGroup (leaflet/canvas only)
            tileLayers:     new Map(),
            dotNetRef:      null,
            renderMode:     mode,
            canvasRenderer: mode === 'canvas' ? L.canvas({ padding: 0.5 }) : null,
            webgl:          null
        };

        if (mode === 'webgl') _initWebGL(divId);
    }

    function destroyMap(divId) {
        const state = _maps[divId];
        if (state) {
            if (state.webgl) {
                if (state.webgl.rafHandle !== null) cancelAnimationFrame(state.webgl.rafHandle);
                state.webgl.canvas.remove();
            }
            state.map.remove();
            delete _maps[divId];
        }
    }

    // ─── Draw Event Handlers ─────────────────────────────────────────────────

    function registerHandlers(divId, dotNetRef) {
        const state = _maps[divId];
        if (!state) return;

        state.dotNetRef = dotNetRef;
        const map = state.map;

        if (!map.pm) {
            console.warn('GeoAssets: Leaflet-Geoman not loaded. Draw tools will not work.');
            return;
        }

        map.pm.setGlobalOptions({ snappable: true, snapDistance: 15 });

        map.on('pm:create', ({ layer }) => {
            const geoJson = JSON.stringify(layer.toGeoJSON());
            layer.remove(); // .NET will re-render via renderFeature
            dotNetRef.invokeMethodAsync('OnFeatureDrawnFromJs', geoJson);
        });

        // Viewport change events — fire after pan/zoom settles
        function emitViewport() {
            const b = map.getBounds();
            dotNetRef.invokeMethodAsync('OnViewportChangedFromJs',
                b.getWest(), b.getSouth(), b.getEast(), b.getNorth());
        }
        map.on('moveend', emitViewport);
        map.on('zoomend', emitViewport);

        map.on('pm:edit', ({ layer }) => {
            const feature = layer.toGeoJSON();
            const id = feature.id || (layer.feature && layer.feature.id);
            if (id) {
                dotNetRef.invokeMethodAsync('OnFeatureEditedFromJs', id, JSON.stringify(feature.geometry));
            }
        });
    }

    // ─── Draw Mode Control ───────────────────────────────────────────────────

    function enableDraw(divId, geometryType) {
        const state = _maps[divId];
        if (!state || !state.map.pm) return;
        const modes = { Point: 'Marker', LineString: 'Line', Polygon: 'Polygon' };
        const mode = modes[geometryType];
        if (mode) state.map.pm.enableDraw(mode);
    }

    function disableDraw(divId) {
        const state = _maps[divId];
        if (!state || !state.map.pm) return;
        state.map.pm.disableDraw();
        state.map.pm.disableGlobalEditMode();
    }

    // ─── Feature Rendering ───────────────────────────────────────────────────

    /** Attaches click / contextmenu callbacks to a Leaflet layer. */
    function _bindEvents(state, layer, id, props) {
        const name = props.name || id;
        layer.bindTooltip && layer.bindTooltip(name, { permanent: false, direction: 'top' });
        layer.on('click', () => {
            state.dotNetRef && state.dotNetRef.invokeMethodAsync('OnFeatureClickedFromJs', id);
        });
        layer.on('contextmenu', (e) => {
            L.DomEvent.stop(e);
            state.dotNetRef && state.dotNetRef.invokeMethodAsync(
                'OnFeatureContextMenuFromJs', id,
                e.originalEvent.clientX, e.originalEvent.clientY);
        });
    }

    /**
     * Renders a feature using Leaflet's SVG or Canvas renderer.
     * Used by renderMode === 'leaflet' and renderMode === 'canvas'.
     */
    function _renderLeaflet(state, feature, color) {
        const id          = feature.id;
        const props       = feature.properties || {};
        const assetTypeId = props.assetTypeId || 'default';
        const extra       = state.canvasRenderer ? { renderer: state.canvasRenderer } : {};

        // Remove stale layer
        if (state.featureLayers.has(id)) {
            const old = state.featureLayers.get(id);
            state.layerGroups.forEach(g => g.removeLayer(old));
            state.map.removeLayer(old);
        }

        const layer = L.geoJSON(feature, {
            ...extra,
            style: () => ({ color, weight: 3, opacity: 0.9, fillOpacity: 0.35 }),
            pointToLayer: (_f, latlng) => L.circleMarker(latlng, {
                radius: 8, color, fillColor: color, fillOpacity: 0.7, weight: 2, ...extra
            }),
            onEachFeature: (_f, l) => _bindEvents(state, l, id, props)
        });

        let group = state.layerGroups.get(assetTypeId);
        if (!group) {
            group = L.layerGroup().addTo(state.map);
            state.layerGroups.set(assetTypeId, group);
        }
        layer.addTo(group);
        state.featureLayers.set(id, layer);
    }

    /**
     * Stores feature data for WebGL rendering and registers an invisible SVG event layer.
     * Does NOT trigger a redraw — call _webglRedraw(divId) when ready.
     */
    function _addWebGLFeature(divId, state, feature, color) {
        const id          = feature.id;
        const props       = feature.properties || {};
        const assetTypeId = props.assetTypeId || 'default';

        // Remove stale event layer
        if (state.featureLayers.has(id))
            state.map.removeLayer(state.featureLayers.get(id));
        state.webgl.features.delete(id);

        // Store for redraw on pan/zoom
        state.webgl.features.set(id, { feature, colorHex: color, assetTypeId });

        // Invisible SVG layer — carries events but is not visible
        const evtLayer = L.geoJSON(feature, {
            style: () => ({ opacity: 0, fillOpacity: 0, weight: 20, interactive: true }),
            pointToLayer: (_f, latlng) =>
                L.circleMarker(latlng, { radius: 12, opacity: 0, fillOpacity: 0, weight: 0, interactive: true }),
            onEachFeature: (_f, l) => _bindEvents(state, l, id, props)
        }).addTo(state.map);

        state.featureLayers.set(id, evtLayer);
    }

    function renderFeature(divId, featureJson, colorMap) {
        const state = _maps[divId];
        if (!state) return;

        const feature = typeof featureJson === 'string' ? JSON.parse(featureJson) : featureJson;
        const color   = (colorMap && colorMap[feature.properties?.assetTypeId]) || '#3388ff';

        if (state.renderMode === 'webgl') {
            _addWebGLFeature(divId, state, feature, color);
            _scheduleRedraw(divId);
        } else {
            _renderLeaflet(state, feature, color);
        }
    }

    function renderAllFeatures(divId, featuresJson) {
        clearAllFeatures(divId);
        const features = typeof featuresJson === 'string' ? JSON.parse(featuresJson) : featuresJson;
        (features || []).forEach(f => renderFeature(divId, f));
    }

    /**
     * Batch-render path: adds all features then does a single WebGL redraw,
     * avoiding redundant redraws after every individual feature.
     */
    function renderFeatureBatch(divId, featuresJson, colorMap) {
        const state    = _maps[divId];
        if (!state) return;
        const features = typeof featuresJson === 'string' ? JSON.parse(featuresJson) : featuresJson;
        if (!features?.length) return;

        if (state.renderMode === 'webgl') {
            features.forEach(f => {
                const color = (colorMap && colorMap[f.properties?.assetTypeId]) || '#3388ff';
                _addWebGLFeature(divId, state, f, color);
            });
            _scheduleRedraw(divId); // one RAF draw covers the entire batch
        } else {
            features.forEach(f => renderFeature(divId, f, colorMap));
        }
    }

    function removeFeature(divId, featureId) {
        const state = _maps[divId];
        if (!state) return;
        const layer = state.featureLayers.get(featureId);
        if (layer) {
            state.layerGroups.forEach(g => g.removeLayer(layer));
            state.map.removeLayer(layer);
            state.featureLayers.delete(featureId);
        }
        if (state.webgl) {
            state.webgl.features.delete(featureId);
            _scheduleRedraw(divId);
        }
    }

    function clearAllFeatures(divId) {
        const state = _maps[divId];
        if (!state) return;
        state.featureLayers.forEach(l => state.map.removeLayer(l));
        state.featureLayers.clear();
        state.layerGroups.forEach(g => { g.clearLayers(); state.map.removeLayer(g); });
        state.layerGroups.clear();
        if (state.webgl) {
            state.webgl.features.clear();
            _scheduleRedraw(divId);
        }
    }

    // ─── Tile / WMS Layers ───────────────────────────────────────────────────

    function addTileLayer(divId, layerId, url, options) {
        const state = _maps[divId];
        if (!state || state.tileLayers.has(layerId)) return; // idempotent
        const layer = L.tileLayer(url, {
            attribution: options?.attribution || '',
            maxZoom:     options?.maxZoom     ?? 19,
            minZoom:     options?.minZoom     ?? 0,
            opacity:     options?.opacity     ?? 1.0
        });
        layer.addTo(state.map);
        state.tileLayers.set(layerId, layer);
    }

    function removeTileLayer(divId, layerId) {
        const state = _maps[divId];
        if (!state) return;
        const layer = state.tileLayers.get(layerId);
        if (layer) {
            state.map.removeLayer(layer);
            state.tileLayers.delete(layerId);
        }
    }

    // ─── Layer Visibility ────────────────────────────────────────────────────

    function setLayerVisibility(divId, assetTypeId, visible) {
        const state = _maps[divId];
        if (!state) return;

        if (state.webgl) {
            // WebGL mode: track visibility set, toggle event layers, redraw
            visible ? state.webgl.hiddenTypes.delete(assetTypeId)
                    : state.webgl.hiddenTypes.add(assetTypeId);
            state.webgl.features.forEach(({ assetTypeId: tid }, fid) => {
                if (tid !== assetTypeId) return;
                const evtLayer = state.featureLayers.get(fid);
                if (!evtLayer) return;
                visible ? evtLayer.addTo(state.map) : state.map.removeLayer(evtLayer);
            });
            _scheduleRedraw(divId);
        } else {
            // Leaflet / Canvas mode: toggle the LayerGroup
            const group = state.layerGroups.get(assetTypeId);
            if (!group) return;
            visible ? state.map.addLayer(group) : state.map.removeLayer(group);
        }
    }

    // ─── View ────────────────────────────────────────────────────────────────

    function fitBounds(divId, bbox) {
        const state = _maps[divId];
        // bbox = [minLon, minLat, maxLon, maxLat]
        if (state && bbox && bbox.length >= 4) {
            state.map.fitBounds([[bbox[1], bbox[0]], [bbox[3], bbox[2]]], { padding: [20, 20] });
        }
    }

    function panToFeature(divId, featureId) {
        const state = _maps[divId];
        if (!state) return;
        const layer = state.featureLayers.get(featureId);
        if (!layer) return;
        if (layer.getBounds) {
            const bounds = layer.getBounds();
            if (bounds.isValid()) {
                state.map.fitBounds(bounds, { maxZoom: 16, padding: [20, 20] });
                return;
            }
        }
        // Try to get center from the geoJSON layer children
        const layers = [];
        layer.eachLayer && layer.eachLayer(l => layers.push(l));
        const first = layers[0];
        if (first && first.getLatLng) {
            state.map.setView(first.getLatLng(), 16);
        }
    }

    // ─── Browser Storage (Web only) ──────────────────────────────────────────

    function localStorageSave(key, value) { localStorage.setItem(key, value); }
    function localStorageLoad(key)        { return localStorage.getItem(key); }

    async function openGeoJsonFilePicker() {
        const [handle] = await window.showOpenFilePicker({
            types: [{
                description: 'GeoJSON Files',
                accept: { 'application/geo+json': ['.geojson', '.json'] }
            }],
            multiple: false
        });
        const file = await handle.getFile();
        return await file.text();
    }

    async function saveGeoJsonFilePicker(content, suggestedName) {
        const handle = await window.showSaveFilePicker({
            suggestedName: suggestedName || 'export.geojson',
            types: [{
                description: 'GeoJSON Files',
                accept: { 'application/geo+json': ['.geojson'] }
            }]
        });
        const writable = await handle.createWritable();
        await writable.write(content);
        await writable.close();
    }

    function downloadAsFile(content, fileName) {
        const blob = new Blob([content], { type: 'application/geo+json' });
        const url  = URL.createObjectURL(blob);
        const a    = Object.assign(document.createElement('a'), { href: url, download: fileName });
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    return {
        initializeMap,
        destroyMap,
        registerHandlers,
        enableDraw,
        disableDraw,
        renderFeature,
        renderAllFeatures,
        renderFeatureBatch,
        removeFeature,
        clearAllFeatures,
        addTileLayer,
        removeTileLayer,
        setLayerVisibility,
        fitBounds,
        panToFeature,
        localStorageSave,
        localStorageLoad,
        openGeoJsonFilePicker,
        saveGeoJsonFilePicker,
        downloadAsFile
    };
})();
