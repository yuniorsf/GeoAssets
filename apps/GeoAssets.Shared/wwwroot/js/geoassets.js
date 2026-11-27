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
    //           renderMode, canvasRenderer?, webgl?, featureStates, highlights }
    const _maps = {};

    // ─── Per-feature visual states (XD01-309) ────────────────────────────────
    // One table drives every render mode. `token` is a CSS custom property (geoassets.css,
    // themed for Mocha and Latte) with `fallback` used when it is missing or not a hex colour.
    // Leaflet/Canvas: color + fillColor, weight (null = keep the feature's own), opacity = alpha,
    // fillOpacity = fillAlpha. WebGL: colour and alpha ONLY — that renderer has no per-feature
    // weight, point size, dash or icon, so `weight` is ignored there.
    const _STATE_STYLES = {
        selected:    { token: '--map-state-selected',    fallback: '#89b4fa', weight: 5,    alpha: 1,    fillAlpha: 0.55 },
        highlighted: { token: '--map-state-highlighted', fallback: '#f9e2af', weight: 5,    alpha: 1,    fillAlpha: 0.55 },
        conflict:    { token: '--map-state-conflict',    fallback: '#f38ba8', weight: 5,    alpha: 1,    fillAlpha: 0.55 },
        disabled:    { token: '--map-state-disabled',    fallback: '#6c7086', weight: null, alpha: 0.25, fillAlpha: 0.1 },
        pending:     { token: '--map-state-pending',     fallback: '#cba6f7', weight: 5,    alpha: 1,    fillAlpha: 0.55 },
        done:        { token: '--map-state-done',        fallback: '#a6e3a1', weight: 5,    alpha: 1,    fillAlpha: 0.55 }
    };
    const _STATE_CLASS_PREFIX = 'geoassets-state-';
    const _HEX_COLOR = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i;

    // Token colours are read from the document once per theme (data-bs-theme on <html>), not per
    // feature. Features already styled keep the colour they were given until they are next
    // re-styled or re-rendered.
    let _stateColors = { theme: null, byState: {} };

    function _stateColor(name) {
        const theme = document.documentElement.getAttribute('data-bs-theme') || '';
        if (_stateColors.theme !== theme) _stateColors = { theme, byState: {} };
        let color = _stateColors.byState[name];
        if (!color) {
            const def = _STATE_STYLES[name];
            const css = getComputedStyle(document.documentElement).getPropertyValue(def.token).trim();
            color = _HEX_COLOR.test(css) ? css : def.fallback;
            _stateColors.byState[name] = color;
        }
        return color;
    }

    /** The state a feature is drawn with: the transient highlight overlay wins over the
     *  persistent registry entry; undefined means "the feature's own style". */
    function _effectiveState(state, id) {
        return state.highlights.has(id) ? 'highlighted' : state.featureStates.get(id);
    }

    /** Styles one Leaflet primitive for `name` (undefined = restore its own style). Vector paths
     *  (including circle markers) are restyled; icon markers, which have no style, get the
     *  geoassets-state-{name} class instead. */
    function _styleSubLayer(sub, name) {
        if (sub.setStyle) {
            if (!sub._geoAssetsBaseStyle) {
                if (!name) return; // never styled — nothing to restore
                const o = sub.options;
                sub._geoAssetsBaseStyle = {
                    color: o.color, fillColor: o.fillColor, weight: o.weight,
                    opacity: o.opacity, fillOpacity: o.fillOpacity
                };
            }
            const base = sub._geoAssetsBaseStyle;
            if (!name) { sub.setStyle(base); return; }
            const def = _STATE_STYLES[name], color = _stateColor(name);
            sub.setStyle({
                color, fillColor: color, weight: def.weight ?? base.weight,
                opacity: def.alpha, fillOpacity: def.fillAlpha
            });
        } else if (sub.options?.icon) {
            const cls = name ? _STATE_CLASS_PREFIX + name : '';
            // Leaflet rebuilds the <img> (and its class list) from the icon options whenever the
            // marker is re-added to the map, e.g. after a layer-visibility toggle.
            sub.options.icon.options.className = cls;
            const el = sub._icon;
            if (!el) return;
            [...el.classList].filter(c => c.startsWith(_STATE_CLASS_PREFIX)).forEach(c => el.classList.remove(c));
            if (cls) el.classList.add(cls);
        }
    }

    /** Re-applies the effective state of one feature to its Leaflet layer. A no-op for a feature
     *  that is not rendered (the registry keeps its state for later) and in WebGL mode, where
     *  featureLayers only holds invisible event layers and state is resolved in _webglRedraw. */
    function _refreshFeatureStyle(state, id) {
        if (state.renderMode === 'webgl') return;
        const layer = state.featureLayers.get(id);
        if (!layer) return;
        const name = _effectiveState(state, id);
        _forEachSnapCandidate(layer, sub => _styleSubLayer(sub, name));
    }

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
        // Premultiplied output: the canvas is composited over the map as premultiplied alpha.
        void main() { gl_FragColor = vec4(u_color.rgb * u_color.a, u_color.a); }`;

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
     * Ear-clipping triangulation for a polygon: an exterior ring and, optionally, the interior
     * rings (holes) to leave unfilled. Each ring may be open or closed (RFC 7946 repeats the
     * first position as the last) and wound either way. Returns a Float32Array of flat triangle
     * vertices [x0,y0, x1,y1, x2,y2, …].
     * A hole with nothing to cut (fewer than 3 distinct positions, no area) or that lies outside
     * the exterior ring is ignored and the rest of the polygon is still filled. A hole that
     * crosses the exterior ring or another hole is not cut exactly.
     */
    function _earcut(ring, holes = []) {
        const xs = [], ys = [];

        // Appends a ring without its repeated positions — the closing one, and any consecutive
        // run. A repeated position sits on a corner of every candidate ear next to it and would
        // veto that ear.
        function addRing(pts) {
            const from = xs.length;
            for (const [x, y] of pts) {
                const k = xs.length - 1;
                if (k < from || xs[k] !== x || ys[k] !== y) { xs.push(x); ys.push(y); }
            }
            while (xs.length > from + 1 && xs[from] === xs[xs.length - 1] && ys[from] === ys[ys.length - 1]) { xs.pop(); ys.pop(); }
            const n = xs.length - from;

            // Signed area gives the winding; a ring with no area has nothing to fill (or cut).
            let area = 0;
            for (let i = 0; i < n; i++) {
                const p = from + i, q = from + (i + 1) % n;
                area += xs[p] * ys[q] - xs[q] * ys[p];
            }
            return { from, n, area };
        }

        const outer = addRing(ring);
        if (outer.n < 3 || outer.area === 0) return new Float32Array(0);
        const cuts = holes.map(addRing).filter(h => h.n >= 3 && h.area !== 0);

        // Linked ring of the vertices still to clip, always walked CCW whatever the input winding.
        // Each hole starts as a ring of its own, walked CW, and is spliced into the outer ring
        // below, which adds up to 3 vertices per hole: the point of an edge the bridge lands on,
        // and a second copy of each end of the bridge.
        const prev = new Int32Array(xs.length + 3 * cuts.length), next = new Int32Array(prev.length);
        for (const { from, n, area } of [outer, ...cuts]) {
            const forward = (area > 0) === (from === 0);
            for (let i = 0; i < n; i++) {
                const before = from + (i + n - 1) % n, after = from + (i + 1) % n;
                prev[from + i] = forward ? before : after;
                next[from + i] = forward ? after : before;
            }
        }

        // > 0 when o → p → q turns left, which is the convex side of the CCW walk.
        const cross = (o, p, q) => (xs[p] - xs[o]) * (ys[q] - ys[o]) - (ys[p] - ys[o]) * (xs[q] - xs[o]);
        const link = (i, j) => { next[i] = j; prev[j] = i; };
        const add = (x, y) => { xs.push(x); ys.push(y); return xs.length - 1; };
        // True when vertex t lies in (or on the edge of) the ring's interior angle at vertex i.
        function faces(i, t) {
            const l = cross(i, next[i], t) >= 0, r = cross(i, t, prev[i]) >= 0;
            return cross(prev[i], i, next[i]) > 0 ? l && r : l || r;
        }

        // Cut each hole out by bridging it into the ring: from the hole's leftmost vertex m go
        // straight left to the nearest point of the ring, then walk ring → bridge → hole → bridge
        // back → ring. Leftmost holes first, so a bridge never has to cross a hole not yet joined.
        const SLACK = 1e-6;    // px a hole may poke through the ring and still count as touching it
        for (const h of cuts) {
            h.m = h.from;
            for (let i = h.from; i < h.from + h.n; i++) if (xs[i] < xs[h.m]) h.m = i;
        }
        cuts.sort((p, q) => xs[p.m] - xs[q.m]);
        let left = outer.n;
        for (const { m, n } of cuts) {
            const mx = xs[m], my = ys[m];
            let at = -1, hitX = -Infinity, onEdge = false, inside = false;
            for (let i = 0, k = 0; k < left; k++, i = next[i]) {
                const j = next[i];
                let x, edge = false, ok;
                if (ys[i] === my) {
                    // A vertex on the way. Several may share the position (an earlier bridge, a
                    // touching hole): take the one whose interior angle the hole lies in.
                    x = xs[i];
                    ok = faces(i, x === mx ? next[m] : m);
                } else if (ys[j] !== my && (ys[i] < my) !== (ys[j] < my)) {
                    x = xs[i] + (my - ys[i]) * (xs[j] - xs[i]) / (ys[j] - ys[i]);
                    edge = true;
                    ok = ys[j] < ys[i];    // the edge has the interior on m's side
                } else continue;
                if (x > mx + SLACK || x < hitX || (x === hitX && !ok)) continue;
                hitX = x; at = i; onEdge = edge; inside = ok;
            }
            // The nearest point faces away (or there is none): the hole is not inside. Skip it.
            if (!inside) continue;

            if (onEdge) {
                const s = add(Math.min(hitX, mx), my);
                link(s, next[at]);
                link(at, s);
                at = s;
                left++;
            }
            if (xs[at] === mx && ys[at] === my) {
                // The hole touches the ring here: no bridge, the ring just detours round the hole.
                const after = next[at];
                link(at, next[m]);
                link(m, after);
                left += n;
            } else {
                const at2 = add(xs[at], ys[at]), m2 = add(mx, my);
                link(at2, next[at]);
                link(prev[m], m2);
                link(m2, at2);
                link(at, m);
                left += n + 2;
            }
        }
        const bridged = left > outer.n;

        function ptInTri(ax, ay, bx, by, cx, cy, px, py) {
            const d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            const d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            const d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
        }

        // Once holes are spliced in, a vertex sitting on corner o of the ear (whose other corners
        // are u then v, CCW) blocks it if the ring leaves that vertex into the ear: a hole or
        // another part of the ring touches there. Bridge ends and plain repeats do not block.
        const cutsInto = (o, u, v, p) => bridged && [prev[p], next[p]].some(q => cross(o, u, q) > 0 && cross(o, q, v) > 0);

        const verts = [];
        let b = 0, misses = 0;

        while (left >= 3) {
            const a = prev[b], c = next[b];
            const ax = xs[a], ay = ys[a], bx = xs[b], by = ys[b], cx = xs[c], cy = ys[c];
            const turn = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            // After a full lap without a clean ear (a ring that touches or crosses itself) clip
            // the next convex corner regardless, and after a second lap any corner, so the loop
            // always ends and the rest of the ring is still filled.
            let ear = turn > 0;
            if (ear && misses < left) {
                const minX = Math.min(ax, bx, cx), maxX = Math.max(ax, bx, cx);
                const minY = Math.min(ay, by, cy), maxY = Math.max(ay, by, cy);
                for (let p = next[c]; p !== a; p = next[p]) {
                    const px = xs[p], py = ys[p];
                    if (px < minX || px > maxX || py < minY || py > maxY) continue;
                    const blocks = px === ax && py === ay ? cutsInto(a, b, c, p)
                                 // … or if a or c is the tip of a spike back to b: nothing to fill.
                                 : px === bx && py === by ? bridged && (p === next[c] || p === prev[a] || cutsInto(b, c, a, p))
                                 : px === cx && py === cy ? cutsInto(c, a, b, p)
                                 : ptInTri(ax, ay, bx, by, cx, cy, px, py);
                    if (blocks) { ear = false; break; }
                }
            }
            // A collinear vertex (turn === 0) spans no area: remove it without a triangle. With
            // holes, one the ring runs straight through stays until a lap finds no ear (a hole may
            // touch the ring exactly there); one it doubles back on (a spike, a repeat) still goes.
            const flat = turn === 0 && (!bridged || misses >= left || (ax - bx) * (cx - bx) + (ay - by) * (cy - by) >= 0);
            if (ear || flat || misses >= 2 * left) {
                if (turn > 0) verts.push(ax, ay, bx, by, cx, cy);
                next[a] = c;
                prev[c] = a;
                left--;
                misses = 0;
            } else {
                misses++;
            }
            b = c;
        }
        return new Float32Array(verts);
    }

    function _projectRing(map, coords) {
        return coords.map(([lng, lat]) => {
            const p = map.latLngToContainerPoint(L.latLng(lat, lng));
            return [p.x, p.y];
        });
    }

    /** Like _projectRing but without snapping to whole pixels: snapping can fold a simple ring
     *  onto itself at low zoom, and ear clipping is only exact for a ring that stays simple. */
    function _projectRingExact(map, coords) {
        const origin = map.getPixelOrigin();
        return coords.map(([lng, lat]) => {
            const p = map.layerPointToContainerPoint(map.project(L.latLng(lat, lng)).subtract(origin));
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

        const gl = canvas.getContext('webgl', { alpha: true, antialias: true, premultipliedAlpha: true });
        if (!gl) {
            console.error('GeoAssets: WebGL unavailable — falling back is not automatic; reload with a different RenderMode.');
            canvas.remove();
            return;
        }

        gl.clearColor(0, 0, 0, 0);
        gl.enable(gl.BLEND);
        // Source-over for premultiplied colour (see _FRAG_SRC). SRC_ALPHA here would square the
        // alpha written to the buffer and let the page show through too strongly.
        gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);

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
        const { canvas, gl, prog, locs, features } = wgl;
        gl.viewport(0, 0, canvas.width, canvas.height);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.useProgram(prog);
        gl.uniform2f(locs.res, canvas.width, canvas.height);
        // Two passes so stateful features (selected, conflict, highlight overlay, …) are never
        // hidden under their unstyled neighbours. State is colour/alpha only in this renderer.
        const stateful = [];
        const drawn = new Map(); // assetTypeId → is the type drawn right now (user toggle × zoom range)
        features.forEach(({ feature, colorHex, assetTypeId }, id) => {
            let show = drawn.get(assetTypeId);
            if (show === undefined) drawn.set(assetTypeId, show = _isTypeDrawn(state, assetTypeId));
            if (!show) return;
            const name = _effectiveState(state, id);
            if (name) stateful.push([feature, name]);
            else _webglDrawGeoFeature(state, feature, colorHex);
        });
        stateful.forEach(([feature, name]) => {
            const def = _STATE_STYLES[name];
            _webglDrawGeoFeature(state, feature, _stateColor(name), def.alpha, def.fillAlpha);
        });
        // Record the anchor for the pan-offset optimisation
        wgl.anchorLatLng = state.map.getCenter();
        wgl.anchorPx     = state.map.latLngToContainerPoint(wgl.anchorLatLng);
    }

    /** `alpha` applies to points, lines and outlines; `fillAlpha` to polygon interiors. */
    function _webglDrawGeoFeature(state, feature, hex, alpha = 0.9, fillAlpha = 0.35) {
        const { type, coordinates: c } = feature.geometry || {};
        if (!type) return;
        if      (type === 'Point')           _wglPoint(state, c, hex, alpha);
        else if (type === 'MultiPoint')      c.forEach(p  => _wglPoint(state, p, hex, alpha));
        else if (type === 'LineString')      _wglLine(state, c, hex, alpha);
        else if (type === 'MultiLineString') c.forEach(l  => _wglLine(state, l, hex, alpha));
        else if (type === 'Polygon')         _wglPolygon(state, c, hex, alpha, fillAlpha);
        else if (type === 'MultiPolygon')    c.forEach(r  => _wglPolygon(state, r, hex, alpha, fillAlpha));
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

    function _wglPoint(state, coord, hex, alpha) {
        const { gl, locs } = state.webgl;
        const p = state.map.latLngToContainerPoint(L.latLng(coord[1], coord[0]));
        gl.uniform1f(locs.ptSize, 10);
        _wglDraw(state, new Float32Array([p.x, p.y]), gl.POINTS, _hexToVec4(hex, alpha));
    }

    function _wglLine(state, coords, hex, alpha) {
        const { gl } = state.webgl;
        const flat = _projectRing(state.map, coords).flat();
        _wglDraw(state, new Float32Array(flat), gl.LINE_STRIP, _hexToVec4(hex, alpha));
    }

    function _wglPolygon(state, rings, hex, alpha, fillAlpha) {
        const { gl } = state.webgl;
        // Filled triangulation of the exterior ring with the holes cut out
        const [outer, ...holes] = rings.map(r => _projectRingExact(state.map, r));
        const fill = _earcut(outer, holes);
        if (fill.length > 0)
            _wglDraw(state, fill, gl.TRIANGLES, _hexToVec4(hex, fillAlpha));

        // Outline for all rings (including holes)
        for (const ring of rings) {
            const proj  = _projectRing(state.map, ring);
            const close = [...proj.flat(), proj[0][0], proj[0][1]];
            _wglDraw(state, new Float32Array(close), gl.LINE_STRIP, _hexToVec4(hex, alpha));
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
            webgl:          null,
            snapScope:      null,       // Set<assetTypeId> | null — null means unscoped/global (XD01-119)
            featureStates:  new Map(),  // featureId → persistent state name; sticky across re-renders (XD01-309)
            highlights:     new Set(),  // featureIds with the transient highlight overlay
            selectionTool:  'none',     // 'none' | 'click' | 'box' | 'fence' — the armed selection tool (XD01-310)
            selectionSaved: null,       // snapping / box-zoom settings to restore once an area tool is disarmed
            selectionInput: null,       // DOM listeners added by _bindSelectionInput, removed in destroyMap
            additive:       false,      // was Shift held on the most recent pointer press on the map?
            hiddenTypes:    new Set(),  // assetTypeIds the user switched off (setLayerVisibility); sticky across re-renders
            zoomRanges:     new Map(),  // assetTypeId → { min, max } (null = open bound) — setLayerZoomRange (XD01-311)
            selectionOutline: null,     // the fence-outline overlay of showSelectionOutline, if any (XD01-311)
            referenceLayers: new Map(), // layerId → another organization's reference layer (XD01-331)
            renderGeneration: 0,        // bumped by beginViewportRender/clearAllFeatures; a batch drawn under an older one stops (XD01-429)
            viewportPass:   null,       // { generation, seen: Set<featureId> } while a viewport pass is open (XD01-429)
            featureSignatures: new Map() // featureId → signature of the data + style a batch last drew it with (XD01-429)
        };

        // A zoom range is re-evaluated whenever the zoom settles. Registered here, not in
        // registerHandlers, so it runs before that function's viewport callback asks .NET for the
        // new viewport's features, and also when Geoman is not loaded.
        map.on('zoomend', () => {
            const state = _maps[divId];
            if (state && state.zoomRanges.size > 0) _applyTypeVisibility(divId, state);
            if (state && state.referenceLayers.size > 0) _syncReferenceLabels(state);
        });

        if (mode === 'webgl') _initWebGL(divId);
    }

    function invalidateSize(divId) {
        _maps[divId]?.map.invalidateSize();
    }

    // Re-centers/re-zooms an already-initialized map — initializeMap is idempotent (a no-op on
    // an existing divId), so it can't be reused to apply a Project's ViewState on open/discard.
    function setView(divId, lat, lon, zoom) {
        _maps[divId]?.map.setView([lat, lon], zoom);
    }

    function destroyMap(divId) {
        const state = _maps[divId];
        if (state) {
            if (state.webgl) {
                if (state.webgl.rafHandle !== null) cancelAnimationFrame(state.webgl.rafHandle);
                state.webgl.canvas.remove();
            }
            _unbindSelectionInput(state);
            state.map.remove();
            state.featureStates.clear();
            state.highlights.clear();
            state.hiddenTypes.clear();
            state.zoomRanges.clear();
            state.selectionOutline = null;
            state.referenceLayers.clear();
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
            // A shape drawn with a selection tool only describes an area: it never becomes a
            // feature, so it goes to its own callback and the tool stays armed (XD01-310).
            if (_isAreaTool(state.selectionTool)) {
                const tool  = state.selectionTool;
                const rings = JSON.stringify(layer.toGeoJSON().geometry.coordinates);
                layer.remove();
                dotNetRef.invokeMethodAsync('OnSelectionGeometryFromJs', tool, rings, state.additive);
                // Geoman disables the draw right after this event, so re-arm on the next tick.
                setTimeout(() => {
                    if (_maps[divId] === state && state.selectionTool === tool && !map.pm.globalDrawModeEnabled())
                        _armAreaDraw(state);
                }, 0);
                return;
            }

            const geoJson = JSON.stringify(layer.toGeoJSON());
            layer.remove(); // .NET will re-render via renderFeature
            dotNetRef.invokeMethodAsync('OnFeatureDrawnFromJs', geoJson);
        });

        _bindSelectionInput(state);

        // Viewport change events — fire after pan/zoom settles. A zoom fires zoomend and moveend back to back: both
        // become one call, carrying the zoom; MapContainer debounces the calls before fetching (XD01-429).
        let viewportQueued = false;
        function emitViewport() {
            if (viewportQueued) return;
            viewportQueued = true;
            queueMicrotask(() => {
                viewportQueued = false;
                if (_maps[divId] !== state) return; // destroyed meanwhile
                const b = map.getBounds();
                dotNetRef.invokeMethodAsync('OnViewportChangedFromJs',
                    b.getWest(), b.getSouth(), b.getEast(), b.getNorth(), Math.round(map.getZoom()));
            });
        }
        map.on('moveend zoomend', emitViewport);

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
        if (!mode) return;
        // Feature drawing and the selection tools are mutually exclusive (XD01-310): starting a
        // draw drops the armed selection tool and puts snapping back first.
        _dropSelectionTool(state);
        state.map.pm.enableDraw(mode);
    }

    function disableDraw(divId) {
        const state = _maps[divId];
        if (!state || !state.map.pm) return;
        // While a box/fence tool is armed the active Geoman draw is the selection gesture, which
        // only setSelectionTool may end (Geoman's disableGlobalEditMode ends a draw too).
        if (_isAreaTool(state.selectionTool)) return;
        state.map.pm.disableDraw();
        state.map.pm.disableGlobalEditMode();
    }

    // ─── Selection Tools (XD01-310) ──────────────────────────────────────────
    // Box and fence reuse Geoman's Rectangle / Polygon draw behind state.selectionTool, so
    // pm:create can tell a selection gesture from a feature being drawn. 'click' needs no draw
    // mode: _bindEvents routes feature clicks to the selection callback while it is armed.

    const _SELECTION_TOOLS = ['none', 'click', 'box', 'fence'];

    function _isAreaTool(tool) { return tool === 'box' || tool === 'fence'; }

    function _armAreaDraw(state) {
        state.map.pm.enableDraw(state.selectionTool === 'box' ? 'Rectangle' : 'Polygon');
    }

    /** Snapping would pull a selection corner onto nearby assets, and Leaflet's Shift+drag box
     *  zoom would fight the Shift (additive) modifier — both are off while an area tool is armed. */
    function _prepareForAreaTool(state) {
        const map = state.map;
        const { snappable, snapDistance } = map.pm.getGlobalOptions();
        state.selectionSaved = { snappable, snapDistance, boxZoom: !!(map.boxZoom && map.boxZoom.enabled()) };
        map.pm.setGlobalOptions({ snappable: false });
        if (state.selectionSaved.boxZoom) map.boxZoom.disable();
    }

    function _restoreAfterAreaTool(state) {
        const saved = state.selectionSaved;
        if (!saved) return;
        state.selectionSaved = null;
        state.map.pm.setGlobalOptions({ snappable: saved.snappable, snapDistance: saved.snapDistance });
        if (saved.boxZoom) state.map.boxZoom.enable();
    }

    /** Disarms whatever selection tool is armed, cancelling an in-progress box/fence. */
    function _dropSelectionTool(state) {
        if (_isAreaTool(state.selectionTool)) {
            state.map.pm.disableDraw();
            _restoreAfterAreaTool(state);
        }
        state.selectionTool = 'none';
    }

    /**
     * Arms a selection tool: 'click' (feature clicks toggle), 'box' (Rectangle), 'fence'
     * (Polygon), or 'none' to disarm. Arming any tool cancels an active feature draw; 'none'
     * leaves a feature draw alone (it only ends a selection gesture). An unknown value is treated
     * as 'none'. The tool stays armed until this is called again or a feature draw starts.
     */
    function setSelectionTool(divId, tool) {
        const state = _maps[divId];
        if (!state) return;
        if (!state.map.pm) {
            console.warn('GeoAssets: Leaflet-Geoman not loaded. Selection tools will not work.');
            return;
        }

        let next = String(tool ?? '').toLowerCase();
        if (!_SELECTION_TOOLS.includes(next)) {
            console.warn(`GeoAssets: unknown selection tool '${tool}' treated as 'none'.`);
            next = 'none';
        }

        const wasArea = _isAreaTool(state.selectionTool);
        const isArea  = _isAreaTool(next);

        if (wasArea || next !== 'none') state.map.pm.disableDraw();
        if (wasArea && !isArea) _restoreAfterAreaTool(state);
        if (!wasArea && isArea) _prepareForAreaTool(state);

        state.selectionTool = next;
        if (isArea) _armAreaDraw(state);
    }

    /** Tracks the additive modifier and wires Escape. Capture phase on the container, because the
     *  click that finishes a fence lands on Geoman's first-vertex marker, not on the map. */
    function _bindSelectionInput(state) {
        if (state.selectionInput) return;
        const container = state.map.getContainer();
        const onPress = e => { state.additive = !!e.shiftKey; };
        const onKey = e => {
            // Escape abandons the box/fence being drawn; the tool stays armed for a fresh one.
            if (e.key !== 'Escape' || !_isAreaTool(state.selectionTool)) return;
            state.map.pm.disableDraw();
            _armAreaDraw(state);
        };
        container.addEventListener('mousedown', onPress, true);
        container.addEventListener('click', onPress, true);
        document.addEventListener('keydown', onKey);
        state.selectionInput = { container, onPress, onKey };
    }

    function _unbindSelectionInput(state) {
        const input = state.selectionInput;
        if (!input) return;
        input.container.removeEventListener('mousedown', input.onPress, true);
        input.container.removeEventListener('click', input.onPress, true);
        document.removeEventListener('keydown', input.onKey);
        state.selectionInput = null;
    }

    // ─── Type-Aware Snap Scoping (XD01-119) ─────────────────────────────────
    // Geoman has no built-in "snap to only these layers/groups" option — snapping is either
    // fully global (map.pm.setGlobalOptions) or opted out per individual layer via that layer's
    // own `snapIgnore` option. Scoping to compatible AssetTypes is implemented by walking the
    // existing per-AssetTypeId layerGroups partition (the same one setLayerVisibility uses) and
    // stamping `snapIgnore` on every layer accordingly.

    /** Invokes `cb` for every actual vector layer (Marker/Polyline/Polygon/...) reachable from
     *  `layer`, recursing through nested LayerGroups — a rendered feature is an L.geoJSON
     *  FeatureGroup wrapping the real primitive(s), and Geoman's snap list only inspects
     *  primitives, never the wrapping group. */
    function _forEachSnapCandidate(layer, cb) {
        if (layer instanceof L.LayerGroup) layer.eachLayer(child => _forEachSnapCandidate(child, cb));
        else cb(layer);
    }

    function _applySnapScope(state) {
        state.layerGroups.forEach((group, assetTypeId) => {
            const ignore = !!state.snapScope && !state.snapScope.has(assetTypeId);
            _forEachSnapCandidate(group, l => { l.options.snapIgnore = ignore; });
        });
    }

    /** Scopes snap targets to only the layers belonging to `allowedTargetAssetTypeIds` (an
     *  empty array is a valid "nothing to snap to" scope, distinct from clearSnapTargetScope's
     *  "unscoped"). Call before enableDraw for the scope to apply to that draw session — Geoman
     *  caches its snap list per draw/edit instance and only rebuilds it once the session's
     *  previous list has been invalidated (draw end / dragend). */
    function setSnapTargetLayer(divId, allowedTargetAssetTypeIds) {
        const state = _maps[divId];
        if (!state) return;
        state.snapScope = new Set(allowedTargetAssetTypeIds || []);
        _applySnapScope(state);
    }

    /** Restores unscoped/global snapping — every rendered layer becomes a valid snap target
     *  again. Call when a type-first draw session ends (feature placed or cancelled). */
    function clearSnapTargetScope(divId) {
        const state = _maps[divId];
        if (!state) return;
        state.snapScope = null;
        _applySnapScope(state);
    }

    // ─── Feature Rendering ───────────────────────────────────────────────────

    /** Attaches click / contextmenu callbacks to a Leaflet layer. */
    function _bindEvents(state, layer, id, props) {
        const name = props.name || id;
        // User data: Leaflet parses a string tooltip as HTML, so the name goes in as a text node (XD01-412).
        const tooltip = document.createElement('span');
        tooltip.textContent = name;
        layer.bindTooltip && layer.bindTooltip(tooltip, { permanent: false, direction: 'top' });
        layer.on('click', (e) => {
            if (!state.dotNetRef) return;
            // While a box/fence is being drawn the click is a corner of that gesture, not a
            // feature click; with the click tool armed it toggles the feature in the selection.
            if (_isAreaTool(state.selectionTool)) return;
            if (state.selectionTool === 'click') {
                state.dotNetRef.invokeMethodAsync(
                    'OnSelectionClickFromJs', id, !!(e.originalEvent && e.originalEvent.shiftKey));
                return;
            }
            state.dotNetRef.invokeMethodAsync('OnFeatureClickedFromJs', id);
        });
        layer.on('contextmenu', (e) => {
            L.DomEvent.stop(e);
            state.dotNetRef && state.dotNetRef.invokeMethodAsync(
                'OnFeatureContextMenuFromJs', id,
                e.originalEvent.clientX, e.originalEvent.clientY);
        });
    }

    /**
     * Resolves the effective style for a feature: the per-feature Layer style resolved
     * server-side (.NET, via LayerResolver) when present, else the pre-existing per-AssetType
     * default (colorMap) — same fallback the app used before per-feature style resolution existed.
     */
    function _effectiveStyle(feature, colorMap, resolvedStyle) {
        if (resolvedStyle) {
            return {
                color: resolvedStyle.color,
                weight: resolvedStyle.weight,
                radius: resolvedStyle.radius,
                fillColor: resolvedStyle.fillColor,
                fillOpacity: resolvedStyle.fillOpacity,
                dashArray: resolvedStyle.dashArray || null,
                iconUrl: resolvedStyle.iconUrl || null
            };
        }
        const color = (colorMap && colorMap[feature.properties?.assetTypeId]) || '#3388ff';
        return { color, weight: 3, radius: 8, fillColor: color, fillOpacity: 0.35, dashArray: null, iconUrl: null };
    }

    /**
     * Renders a feature using Leaflet's SVG or Canvas renderer.
     * Used by renderMode === 'leaflet' and renderMode === 'canvas'.
     */
    function _renderLeaflet(state, feature, style) {
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
            style: () => ({
                color: style.color, weight: style.weight, opacity: 0.9,
                fillColor: style.fillColor, fillOpacity: style.fillOpacity, dashArray: style.dashArray
            }),
            pointToLayer: (_f, latlng) => style.iconUrl
                ? L.marker(latlng, { icon: L.icon({ iconUrl: style.iconUrl, iconSize: [25, 25], iconAnchor: [12, 25] }) })
                : L.circleMarker(latlng, {
                    radius: style.radius, color: style.color, fillColor: style.fillColor,
                    fillOpacity: style.fillOpacity, weight: style.weight, ...extra
                  }),
            onEachFeature: (_f, l) => _bindEvents(state, l, id, props)
        });

        let group = state.layerGroups.get(assetTypeId);
        if (!group) {
            // A type hidden by the user or by its zoom range gets its group too, just not on the
            // map: viewport refreshes rebuild the groups constantly and must not un-hide it.
            group = L.layerGroup();
            if (_isTypeDrawn(state, assetTypeId)) group.addTo(state.map);
            state.layerGroups.set(assetTypeId, group);
        }
        layer.addTo(group);
        state.featureLayers.set(id, layer);

        // Viewport chunking re-creates layers constantly — a state set before this render (or
        // before the feature was ever in view) must be applied to the fresh layer.
        if (_effectiveState(state, id)) _refreshFeatureStyle(state, id);

        // A snap scope may already be active (a type-first draw session started before this
        // feature was rendered/re-rendered) — stamp it onto the new layer so it doesn't fall
        // back to "always snappable" until the next full _applySnapScope pass.
        if (state.snapScope) {
            const ignore = !state.snapScope.has(assetTypeId);
            _forEachSnapCandidate(layer, l => { l.options.snapIgnore = ignore; });
        }
    }

    /**
     * Stores feature data for WebGL rendering and registers an invisible SVG event layer.
     * Does NOT trigger a redraw — call _webglRedraw(divId) when ready.
     * Nothing to re-apply here for per-feature states: _webglRedraw resolves each feature's state
     * from the registry on every draw, so a re-added feature picks its state up automatically.
     */
    function _addWebGLFeature(_divId, state, feature, color) {
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
        });
        // A type that is not drawn (user toggle or zoom range) must not be clickable either.
        if (_isTypeDrawn(state, assetTypeId)) evtLayer.addTo(state.map);

        state.featureLayers.set(id, evtLayer);
    }

    function renderFeature(divId, featureJson, colorMap, resolvedStyle) {
        const state = _maps[divId];
        if (!state) return;

        const feature = typeof featureJson === 'string' ? JSON.parse(featureJson) : featureJson;
        // A single render always redraws; the next batch carrying this feature redraws it once more.
        state.featureSignatures.delete(feature.id);
        _drawFeature(divId, state, feature, _effectiveStyle(feature, colorMap, resolvedStyle));
    }

    function _drawFeature(divId, state, feature, style) {
        if (state.viewportPass) state.viewportPass.seen.add(feature.id);

        if (state.renderMode === 'webgl') {
            // WebGL mode only varies by color today (see _addWebGLFeature) — weight/radius/
            // fillOpacity/dashArray/icon stay at their existing hardcoded values in that mode.
            _addWebGLFeature(divId, state, feature, style.color);
            _scheduleRedraw(divId); // at most one RAF draw is queued, however many features a batch adds
        } else {
            _renderLeaflet(state, feature, style);
        }
    }

    /**
     * FNV-1a hash of a feature's data and resolved style — what a batch compares to skip a feature
     * already drawn exactly so (XD01-429). A hash, not the JSON itself, so the registry stays small.
     */
    function _featureSignature(feature, style) {
        const text = JSON.stringify(feature) + '|' + JSON.stringify(style);
        let hash = 0x811c9dc5;
        for (let i = 0; i < text.length; i++) {
            hash ^= text.charCodeAt(i);
            hash = Math.imul(hash, 0x01000193);
        }
        return (hash >>> 0).toString(36) + ':' + text.length;
    }

    /** Draws one feature of a batch, unless the same data in the same style is already on the map (XD01-429). */
    function _renderBatchFeature(divId, state, feature, colorMap, styleMap) {
        const style     = _effectiveStyle(feature, colorMap, styleMap && styleMap[feature.id]);
        const signature = _featureSignature(feature, style);
        if (state.featureLayers.has(feature.id) && state.featureSignatures.get(feature.id) === signature) {
            if (state.viewportPass) state.viewportPass.seen.add(feature.id);
            return;
        }
        _drawFeature(divId, state, feature, style);
        state.featureSignatures.set(feature.id, signature);
    }

    function renderAllFeatures(divId, featuresJson) {
        clearAllFeatures(divId);
        const features = typeof featuresJson === 'string' ? JSON.parse(featuresJson) : featuresJson;
        (features || []).forEach(f => renderFeature(divId, f));
    }

    // Sub-chunk size for renderFeatureBatch's non-WebGL RAF chunking (XD01-163) — a single
    // requestAnimationFrame tick renders at most this many features before yielding back to the
    // browser, so a large batch (the raw-JSON viewport/pipeline paths can hand this one call
    // thousands of features — see XD01-172/XD01-162) never blocks one frame long enough to stall
    // input, the same way _scheduleRedraw already bounds WebGL's per-frame cost.
    const _RAF_SUB_CHUNK_SIZE = 75;

    /**
     * Batch-render path: adds all features then does a single WebGL redraw,
     * avoiding redundant redraws after every individual feature.
     *
     * Non-WebGL (SVG/Canvas via Leaflet) returns a Promise that resolves only once every feature
     * has rendered — callers (MapInteropService) already treat "call resolved" as "fully
     * rendered" (e.g. MapContainer clears the map before dispatching the next chunk), so
     * completion ordering must be preserved; only how the work is spread across frames changes.
     */
    function renderFeatureBatch(divId, featuresJson, colorMap, styleMap) {
        const state    = _maps[divId];
        if (!state) return;
        const features = typeof featuresJson === 'string' ? JSON.parse(featuresJson) : featuresJson;
        if (!features?.length) return;

        if (state.renderMode === 'webgl') {
            features.forEach(f => _renderBatchFeature(divId, state, f, colorMap, styleMap));
            return;
        }

        // A newer viewport pass or a clear supersedes this batch: its sub-chunks still waiting for a frame are
        // dropped instead of painting a stale viewport (XD01-429).
        const generation = state.renderGeneration;
        return new Promise(resolve => {
            const _batchStart = performance.now();
            let i = 0;
            function renderNextSubChunk() {
                if (_maps[divId] !== state) { resolve(); return; } // map destroyed mid-render
                if (state.renderGeneration !== generation) { resolve(); return; } // superseded mid-render
                const end = Math.min(i + _RAF_SUB_CHUNK_SIZE, features.length);
                for (; i < end; i++) {
                    _renderBatchFeature(divId, state, features[i], colorMap, styleMap);
                }
                if (i < features.length) {
                    requestAnimationFrame(renderNextSubChunk);
                } else {
                    console.log(`[GeoAssets] renderFeatureBatch (non-WebGL) — ${features.length} features in ${(performance.now() - _batchStart).toFixed(1)} ms`);
                    resolve();
                }
            }
            renderNextSubChunk();
        });
    }

    // Neither removeFeature nor clearAllFeatures touches state.featureStates / state.highlights:
    // both run as part of normal viewport refreshes and the features come straight back.
    // Only clearFeatureStates and destroyMap empty the registry.
    function removeFeature(divId, featureId) {
        const state = _maps[divId];
        if (!state) return;
        const layer = state.featureLayers.get(featureId);
        if (layer) {
            state.layerGroups.forEach(g => g.removeLayer(layer));
            state.map.removeLayer(layer);
            state.featureLayers.delete(featureId);
        }
        state.featureSignatures.delete(featureId);
        if (state.webgl) {
            state.webgl.features.delete(featureId);
            _scheduleRedraw(divId);
        }
    }

    function clearAllFeatures(divId) {
        const state = _maps[divId];
        if (!state) return;
        state.renderGeneration++;
        state.viewportPass = null;
        state.featureLayers.forEach(l => state.map.removeLayer(l));
        state.featureLayers.clear();
        state.featureSignatures.clear();
        state.layerGroups.forEach(g => { g.clearLayers(); state.map.removeLayer(g); });
        state.layerGroups.clear();
        if (state.webgl) {
            state.webgl.features.clear();
            _scheduleRedraw(divId);
        }
    }

    // ─── Viewport diff rendering (XD01-429) ──────────────────────────────────
    // A viewport refresh no longer clears the map. beginViewportRender opens a pass; every feature drawn while it is
    // open — by a batch or by a single renderFeature — is marked seen (a batch skips one already drawn with the same
    // data and style, keeping its layer, its per-feature state and any edit in progress); endViewportRender then
    // removes only the features the pass did not see. Opening a pass, or clearAllFeatures, supersedes the open one:
    // its batches stop at their next frame and its end removes nothing. Reference layers, tile/WMS layers and the
    // per-feature state registry are never touched.

    function beginViewportRender(divId) {
        const state = _maps[divId];
        if (!state) return 0;
        state.renderGeneration++;
        state.viewportPass = { generation: state.renderGeneration, seen: new Set() };
        return state.renderGeneration;
    }

    function endViewportRender(divId, generation) {
        const state = _maps[divId];
        const pass  = state?.viewportPass;
        if (!pass || pass.generation !== generation) return;
        state.viewportPass = null;
        const stale = [];
        state.featureLayers.forEach((_layer, id) => { if (!pass.seen.has(id)) stale.push(id); });
        state.webgl?.features.forEach((_entry, id) => {
            if (!pass.seen.has(id) && !state.featureLayers.has(id)) stale.push(id);
        });
        stale.forEach(id => removeFeature(divId, id));
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

    // ─── OGC WMS Layers ──────────────────────────────────────────────────────
    // XD01-96: a WMS layer on this app's own server needs the caller's bearer token, which an <img src> request can't
    // carry. Such a layer's tiles are fetched with an Authorization header and handed to Leaflet as object URLs; the
    // token never appears in a URL. Whether a layer needs a token, and which one, is asked of MapContainer's
    // GetTileAccessFromJs (IMapTileAccessProvider): a layer on any other WMS server gets no token and loads its tiles
    // with plain image requests, as before. A layer that needs a token none is available for shows nothing.

    // Ask for a fresh token this long before the current one expires.
    const TILE_TOKEN_REFRESH_MARGIN_MS = 60 * 1000;
    // How long a "no token available" answer is kept before asking again (e.g. after the user signs in).
    const TILE_ACCESS_UNAVAILABLE_HOLD_MS = 30 * 1000;

    // One per layer: the access answer for its URL, shared by all its tiles, with one request in flight at a time.
    function createWmsTileAccess(state, wmsBaseUrl) {
        let access     = null;
        let pending    = null;
        let generation = 0;   // numbers each answer, so a tile can tell whether its refused one was already replaced

        function isCurrent(a) {
            if (!a) return false;
            if (!a.requiresToken) return true;
            if (!a.accessToken) return Date.now() - a.receivedAt < TILE_ACCESS_UNAVAILABLE_HOLD_MS;
            return !a.expiresOn || Date.parse(a.expiresOn) - TILE_TOKEN_REFRESH_MARGIN_MS > Date.now();
        }

        function stamp(answer) {
            return Object.assign({}, answer, { receivedAt: Date.now(), generation: ++generation });
        }

        function unavailable() {
            return stamp({ requiresToken: true, accessToken: null, expiresOn: null });
        }

        // refused: the answer a tile was just refused with (401). When another tile already replaced it, the replacement
        // is used instead of asking again — MSAL may well hand back the same cached token string.
        function get(refused) {
            const replace = refused !== undefined && access && access.generation === refused.generation;
            if (!replace && isCurrent(access)) return Promise.resolve(access);
            if (pending) return pending;
            if (!state.dotNetRef) return Promise.resolve(unavailable());
            pending = state.dotNetRef.invokeMethodAsync('GetTileAccessFromJs', wmsBaseUrl)
                .then(answer => stamp(answer), () => unavailable())
                .then(answer => { access = answer; pending = null; return answer; });
            return pending;
        }

        // The server refused a replaced token too: stop asking it for this layer for a while.
        function reject(refused) {
            if (access && access.generation === refused.generation) access = unavailable();
        }

        return { get, reject };
    }

    function releaseWmsTile(tile) {
        if (!tile) return;
        if (tile._geoAbort) { tile._geoAbort.abort(); tile._geoAbort = null; }
        if (tile._geoObjectUrl) { URL.revokeObjectURL(tile._geoObjectUrl); tile._geoObjectUrl = null; }
    }

    async function fetchWmsTile(tileAccess, url, signal) {
        let access = await tileAccess.get();
        for (let attempt = 0; ; attempt++) {
            if (!access.requiresToken) return { direct: true };
            if (!access.accessToken) return { error: 'No access token is available for this map layer.' };

            const response = await fetch(url, {
                headers:     { Authorization: 'Bearer ' + access.accessToken },
                credentials: 'omit',
                signal
            });
            if (response.status === 401 && attempt === 0) {
                access = await tileAccess.get(access);
                continue;
            }
            if (response.status === 401) tileAccess.reject(access);
            if (!response.ok) return { error: 'The tile request failed with HTTP ' + response.status + '.' };
            return { blob: await response.blob() };
        }
    }

    // A tile of a layer whose requests may need the bearer token (the WMS layers above, and the kernel's raster tiles of
    // XD01-436 below): fetched with the Authorization header and shown as an object URL, or requested as a plain image
    // when the layer needs no token.
    function createAuthenticatedTile(coords, done) {
        const tile = document.createElement('img');
        tile.alt = '';
        tile.setAttribute('role', 'presentation');
        L.DomEvent.on(tile, 'load', L.Util.bind(this._tileOnLoad, this, done, tile));
        L.DomEvent.on(tile, 'error', L.Util.bind(this._tileOnError, this, done, tile));
        // The decoded image stays on screen; its object URL isn't needed once the image loaded or failed.
        L.DomEvent.on(tile, 'load error', () => {
            if (tile._geoObjectUrl) { URL.revokeObjectURL(tile._geoObjectUrl); tile._geoObjectUrl = null; }
        });

        const url        = this.getTileUrl(coords);
        const controller = new AbortController();
        tile._geoAbort = controller;
        fetchWmsTile(this._geoTileAccess, url, controller.signal)
            .then(result => {
                if (controller.signal.aborted) return;
                tile._geoAbort = null;
                if (result.direct) { tile.src = url; return; }
                if (result.error) { done(new Error(result.error), tile); return; }
                tile._geoObjectUrl = URL.createObjectURL(result.blob);
                tile.src = tile._geoObjectUrl;
            })
            .catch(err => {
                if (controller.signal.aborted) return;
                tile._geoAbort = null;
                done(err, tile);
            });
        return tile;
    }

    const AuthenticatedWmsLayer = L.TileLayer.WMS.extend({
        initialize: function (url, options, tileAccess) {
            L.TileLayer.WMS.prototype.initialize.call(this, url, options);
            this._geoTileAccess = tileAccess;
            // Leaflet discards a tile it no longer needs (pan/zoom/removal): cancel its request, free its object URL.
            this.on('tileunload tileabort', e => releaseWmsTile(e.tile));
        },

        createTile: createAuthenticatedTile
    });

    // An XYZ layer whose tiles are fetched as the WMS layers' are (XD01-436: the kernel's raster tiles).
    const AuthenticatedXyzLayer = L.TileLayer.extend({
        initialize: function (url, options, tileAccess) {
            L.TileLayer.prototype.initialize.call(this, url, options);
            this._geoTileAccess = tileAccess;
            this.on('tileunload tileabort', e => releaseWmsTile(e.tile));
        },

        createTile: createAuthenticatedTile
    });

    function addWmsLayer(divId, layerId, wmsBaseUrl, options) {
        const state = _maps[divId];
        if (!state || state.tileLayers.has(layerId)) return; // idempotent
        const tileAccess = createWmsTileAccess(state, new URL(wmsBaseUrl, document.baseURI).href);
        const layer = new AuthenticatedWmsLayer(wmsBaseUrl, {
            layers:      options?.layers      ?? 'geoassets:feature',
            format:      options?.format      ?? 'image/png',
            transparent: options?.transparent ?? true,
            version:     options?.version     ?? '1.1.1',
            attribution: options?.attribution ?? '',
            maxZoom:     options?.maxZoom     ?? 19,
            opacity:     options?.opacity     ?? 1.0,
            // Force geographic CRS so Leaflet sends BBOX in degrees (EPSG:4326),
            // matching the coordinate system of the PostGIS data on the server.
            crs:         L.CRS.EPSG4326
        }, tileAccess);
        layer.addTo(state.map);
        state.tileLayers.set(layerId, layer);
    }

    function removeWmsLayer(divId, layerId) {
        const state = _maps[divId];
        if (!state) return;
        const layer = state.tileLayers.get(layerId);
        if (layer) {
            state.map.removeLayer(layer);
            state.tileLayers.delete(layerId);
        }
    }

    // ─── The kernel's tiles: the interim Leaflet split (XD01-436) ─────────────
    // HybridLodMapLayerRenderer draws the kernel's provider entry in the Leaflet modes (MapInterop:LeafletTileSplit) as
    // the raster tiles of /tiles/style.json (XD01-434) at the zooms of its raster tier, and as features at the others —
    // .NET decides which from what showTileLayers answers. The style and every tile are fetched with the bearer token
    // GetTileAccessFromJs answers for the style's URL (the tiles are shown as object URLs): the token is never in a URL.
    // A style with no raster tier (the default LOD policy) adds no layer, and the features stay at every zoom.

    const KERNEL_RASTER_SOURCE   = 'geoassets-raster';
    const KERNEL_VERSION_KEY     = 'geoassets:version';
    const KERNEL_MAX_STYLE_ZOOM  = 24;
    const KERNEL_SWAP_TIMEOUT_MS = 10 * 1000;

    // { template, minZoom, maxZoom } of the style's raster tier (maxzoom is exclusive in the style spec), or null.
    function _kernelRasterTier(style) {
        const source = style?.sources?.[KERNEL_RASTER_SOURCE];
        const layer  = (style?.layers || []).find(l => l && l.source === KERNEL_RASTER_SOURCE && l.type === 'raster');
        const template = Array.isArray(source?.tiles) ? source.tiles[0] : null;
        if (!layer || !template) return null;
        const minZoom = Number.isFinite(layer.minzoom) ? layer.minzoom : 0;
        const maxZoom = Number.isFinite(layer.maxzoom) ? layer.maxzoom - 1 : KERNEL_MAX_STYLE_ZOOM;
        return maxZoom >= minZoom ? { template, minZoom, maxZoom } : null;
    }

    async function _fetchKernelStyle(kernel) {
        let access = await kernel.access.get();
        for (let attempt = 0; ; attempt++) {
            if (access.requiresToken && !access.accessToken) return { error: 'no access token is available for ' + kernel.styleUrl };
            const headers = access.accessToken ? { Authorization: 'Bearer ' + access.accessToken } : {};
            // no-cache: the browser revalidates its copy with the style's ETag (304 when {v} did not move).
            const response = await fetch(kernel.styleUrl, { headers, cache: 'no-cache', credentials: 'omit' });
            if (response.status === 401 && attempt === 0) {
                access = await kernel.access.get(access);
                continue;
            }
            if (response.status === 401) kernel.access.reject(access);
            if (!response.ok) return { error: kernel.styleUrl + ' answered HTTP ' + response.status };
            return { style: await response.json() };
        }
    }

    function _kernelLayer(kernel, tier) {
        return new AuthenticatedXyzLayer(tier.template, {
            minZoom: tier.minZoom,
            maxZoom: Math.min(tier.maxZoom, 19),
            tileSize: 256,
            attribution: ''
        }, kernel.access);
    }

    function _sameTier(a, b) {
        return (!a && !b) || (!!a && !!b && a.template === b.template && a.minZoom === b.minZoom && a.maxZoom === b.maxZoom);
    }

    // Applies a (re-)read style; resolves once a swapped layer drew its first tiles (or after a timeout), so the old {v}'s
    // tiles stay on screen until the new ones are there — no blank.
    function _applyKernelStyle(state, kernel, style) {
        kernel.style   = style;
        kernel.version = typeof style?.metadata?.[KERNEL_VERSION_KEY] === 'string' ? style.metadata[KERNEL_VERSION_KEY] : null;
        const tier = _kernelRasterTier(style);
        if (_sameTier(kernel.tier, tier)) return Promise.resolve();

        const old  = kernel.layer;
        const next = tier ? _kernelLayer(kernel, tier) : null;
        kernel.tier  = tier;
        kernel.layer = next;
        if (kernel.hidden || !next) {
            if (old && state.map.hasLayer(old)) state.map.removeLayer(old);
            return Promise.resolve();
        }
        next.addTo(state.map);
        if (!old) return Promise.resolve();
        return new Promise(resolve => {
            let timer = null;
            const swap = () => {
                clearTimeout(timer);
                if (state.map.hasLayer(old)) state.map.removeLayer(old);
                resolve();
            };
            timer = setTimeout(swap, KERNEL_SWAP_TIMEOUT_MS);
            next.once('load', swap);
        });
    }

    function _kernelInfo(state, kernel) {
        return {
            version:       kernel.version,
            rasterMinZoom: kernel.tier ? kernel.tier.minZoom : null,
            rasterMaxZoom: kernel.tier ? kernel.tier.maxZoom : null,
            zoom:          Math.round(state.map.getZoom())
        };
    }

    // Shows the kernel's raster tiles (reading the style first when the map has none yet); answers
    // { version, rasterMinZoom, rasterMaxZoom, zoom } (.NET's MapTileStyleInfo), or null when the style can't be read.
    async function showTileLayers(divId, styleUrl) {
        const state = _maps[divId];
        if (!state || !styleUrl) return null;
        const url = new URL(styleUrl, document.baseURI).href;
        let kernel = state.kernelTiles;
        if (!kernel || kernel.styleUrl !== url) {
            if (kernel?.layer && state.map.hasLayer(kernel.layer)) state.map.removeLayer(kernel.layer);
            kernel = state.kernelTiles = {
                styleUrl: url, access: createWmsTileAccess(state, url),
                style: null, version: null, tier: null, layer: null, hidden: false, loading: null
            };
        }
        kernel.hidden = false;
        if (!kernel.style) {
            kernel.loading = kernel.loading || _fetchKernelStyle(kernel).finally(() => { kernel.loading = null; });
            const result = await kernel.loading.catch(err => ({ error: String(err) }));
            if (_maps[divId] !== state || state.kernelTiles !== kernel) return null;
            if (result.error) {
                console.warn('GeoAssets: the kernel tile style was not loaded — ' + result.error);
                return null;
            }
            if (!kernel.style) _applyKernelStyle(state, kernel, result.style);
        }
        if (!kernel.hidden && kernel.layer && !state.map.hasLayer(kernel.layer)) kernel.layer.addTo(state.map);
        return _kernelInfo(state, kernel);
    }

    function hideTileLayers(divId) {
        const state  = _maps[divId];
        const kernel = state?.kernelTiles;
        if (!kernel) return;
        kernel.hidden = true;
        if (kernel.layer && state.map.hasLayer(kernel.layer)) state.map.removeLayer(kernel.layer);
    }

    // A raster tile can't hide one feature: nothing to filter in the Leaflet split.
    function setHiddenTileFeatures(_divId, _featureIds) { }

    // Re-reads the style (MapTileStyleRefresher: after this client's writes and periodically) and follows a new {v} by
    // swapping the raster layer; answers the current {v} once the new tiles are drawn.
    async function refreshTileStyle(divId) {
        const state  = _maps[divId];
        const kernel = state?.kernelTiles;
        if (!kernel) return null;
        let result;
        try { result = await _fetchKernelStyle(kernel); } catch (err) { result = { error: String(err) }; }
        if (_maps[divId] !== state || state.kernelTiles !== kernel) return null;
        if (result.error) {
            console.warn('GeoAssets: re-reading the kernel tile style failed — ' + result.error);
            return kernel.version;
        }
        await _applyKernelStyle(state, kernel, result.style);
        return kernel.version;
    }

    // ─── Layer Visibility ────────────────────────────────────────────────────
    // Two independent rules decide whether an asset type is drawn, and both must agree:
    //   • the user's own toggle (setLayerVisibility → state.hiddenTypes), and
    //   • the type's zoom range (setLayerZoomRange → state.zoomRanges, XD01-311).
    // Neither rule overwrites the other: a type the user hid stays hidden when the zoom comes back
    // into range, and one hidden by zoom reappears when the zoom returns if the user has it on.
    // Both are sticky per map — they survive clearAllFeatures and viewport re-renders. Hiding only
    // takes layers off the map; feature states (state.featureStates) are untouched and re-apply.

    function _inZoomRange(state, assetTypeId) {
        const range = state.zoomRanges.get(assetTypeId);
        if (!range) return true;
        const zoom = state.map.getZoom();
        return (range.min === null || zoom >= range.min) && (range.max === null || zoom <= range.max);
    }

    function _isTypeDrawn(state, assetTypeId) {
        return !state.hiddenTypes.has(assetTypeId) && _inZoomRange(state, assetTypeId);
    }

    /** Puts on / takes off the map whatever _isTypeDrawn says, for one type or (no id) for all. */
    function _applyTypeVisibility(divId, state, assetTypeId) {
        const sync = (layer, tid) => {
            const show = _isTypeDrawn(state, tid);
            if (show === state.map.hasLayer(layer)) return;
            show ? state.map.addLayer(layer) : state.map.removeLayer(layer);
        };

        if (state.webgl) {
            // WebGL mode: the redraw skips the type; its invisible event layers follow it.
            state.webgl.features.forEach(({ assetTypeId: tid }, fid) => {
                if (assetTypeId !== undefined && tid !== assetTypeId) return;
                const evtLayer = state.featureLayers.get(fid);
                if (evtLayer) sync(evtLayer, tid);
            });
            _scheduleRedraw(divId);
            return;
        }

        // Leaflet / Canvas mode: toggle the type's LayerGroup
        state.layerGroups.forEach((group, tid) => {
            if (assetTypeId === undefined || tid === assetTypeId) sync(group, tid);
        });
    }

    function setLayerVisibility(divId, assetTypeId, visible) {
        const state = _maps[divId];
        if (!state) return;
        visible ? state.hiddenTypes.delete(assetTypeId) : state.hiddenTypes.add(assetTypeId);
        _applyTypeVisibility(divId, state, assetTypeId);
    }

    function _zoomBound(value) {
        if (value === null || value === undefined) return null;
        const n = Number(value);
        return Number.isFinite(n) ? n : null;
    }

    /**
     * Draws `assetTypeId` only while `min <= zoom <= max` (null/undefined bound = open; both open
     * removes the range). Evaluated now and on every `zoomend`. A provider may still send features
     * of every type for the viewport (the kernel leaves the hidden ones out since XD01-430) — any
     * that arrive are kept, just not on the map.
     */
    function setLayerZoomRange(divId, assetTypeId, min, max) {
        const state = _maps[divId];
        if (!state) return;
        const range = { min: _zoomBound(min), max: _zoomBound(max) };
        if (range.min === null && range.max === null) state.zoomRanges.delete(assetTypeId);
        else state.zoomRanges.set(assetTypeId, range);
        _applyTypeVisibility(divId, state, assetTypeId);
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

    // ─── Selection fence outline (XD01-311) ──────────────────────────────────
    // A drawing aid, not a feature: it lives outside featureLayers / layerGroups / the WebGL
    // feature store, so clearAllFeatures, layer visibility, zoom ranges, feature states, snapping
    // and the selection tools never see it. Always SVG (the map's default renderer), in every
    // render mode, so its colour comes from CSS (.geoassets-selection-outline → a theme token)
    // and follows a theme switch without being redrawn.

    function _isPosition(p) {
        return Array.isArray(p) && p.length >= 2 && Number.isFinite(p[0]) && Number.isFinite(p[1]);
    }

    /** `rings`: RFC 7946 polygon rings of [lon, lat] positions, exterior first. Replaces any
     *  outline already shown. Rings that are not drawable are ignored with a warning. */
    function showSelectionOutline(divId, rings) {
        const state = _maps[divId];
        if (!state) return;
        clearSelectionOutline(divId);

        const latLngs = (Array.isArray(rings) ? rings : [])
            .filter(ring => Array.isArray(ring) && ring.length >= 3 && ring.every(_isPosition))
            .map(ring => ring.map(p => [p[1], p[0]]));
        if (latLngs.length === 0) {
            console.warn('GeoAssets: selection outline ignored — no drawable ring.');
            return;
        }

        state.selectionOutline = L.polygon(latLngs, {
            className:   'geoassets-selection-outline',
            color:       'currentColor',   // resolved by the CSS class above
            weight:      3,
            dashArray:   '8 6',
            fill:        false,
            interactive: false,            // no click, hover, tooltip or context menu
            bubblingMouseEvents: false,
            pmIgnore:    true,             // Geoman: never editable, draggable or removable
            snapIgnore:  true              // …and never a snap target
        }).addTo(state.map);
    }

    function clearSelectionOutline(divId) {
        const state = _maps[divId];
        if (!state || !state.selectionOutline) return;
        state.map.removeLayer(state.selectionOutline);
        state.selectionOutline = null;
    }

    // ─── Reference layers of another organization (XD01-331) ─────────────────
    // Another organization's assets, shown for orientation only. Like the selection outline they
    // are not features: they live outside featureLayers / layerGroups / the WebGL feature store,
    // so clearAllFeatures, layer visibility, zoom ranges, feature states, snap scoping, panToFeature
    // and the selection tools never see them. Every layer is non-interactive (no click, context
    // menu, popup or hover tooltip — nothing ever reaches .NET), ignored by Leaflet-Geoman
    // (pmIgnore: never editable, draggable or removable; snapIgnore: never a snap target), and
    // drawn as SVG in its own pane below the user's features in every render mode, its colours
    // coming from CSS theme tokens (.geoassets-reference-*) so a theme switch needs no redraw.

    const _REFERENCE_PANE       = 'geoassetsReference';
    const _REFERENCE_LABEL_PANE = 'geoassetsReferenceLabels';
    const _REFERENCE_DEFAULT_LABEL_MIN_ZOOM = 16;

    /** One muted, uniform style for every reference shape; the colour comes from CSS. */
    const _REFERENCE_STYLE = {
        color:       'currentColor',   // resolved by .geoassets-reference-feature
        weight:      1.5,
        opacity:     1,
        fillColor:   'currentColor',
        fillOpacity: 0.12,
        dashArray:   '4 3'
    };

    /** Options every reference primitive is built with. className must be set at construction. */
    const _REFERENCE_LAYER_OPTIONS = {
        ..._REFERENCE_STYLE,
        pane:        _REFERENCE_PANE,
        className:   'geoassets-reference-feature',
        interactive: false,            // no click, hover, popup, tooltip-on-hover or context menu
        bubblingMouseEvents: false,
        pmIgnore:    true,             // Geoman: never editable, draggable or removable
        snapIgnore:  true              // …and never a snap target
    };

    /** Shapes above the tiles (200) and below the user's own features (overlayPane, 400); their
     *  labels just above the shapes, still below the user's features. Neither takes pointer input. */
    function _ensureReferencePanes(map) {
        if (map.getPane(_REFERENCE_PANE)) return;
        const shapes = map.createPane(_REFERENCE_PANE);
        shapes.style.zIndex = '350';
        shapes.style.pointerEvents = 'none';
        shapes.classList.add('geoassets-reference-pane');
        const labels = map.createPane(_REFERENCE_LABEL_PANE);
        labels.style.zIndex = '390';
        labels.style.pointerEvents = 'none';
        labels.classList.add('geoassets-reference-pane');
    }

    /** The first vector primitive inside `layer` (an L.geoJSON group), which carries the label. */
    function _firstPrimitive(layer) {
        if (!(layer instanceof L.LayerGroup)) return layer;
        for (const child of layer.getLayers()) {
            const found = _firstPrimitive(child);
            if (found) return found;
        }
        return null;
    }

    /** Builds one non-interactive reference shape from a reference feature, or null when its
     *  geometry is not drawable. */
    function _referenceShape(feature) {
        if (!feature || typeof feature !== 'object' || !feature.geometry) return null;
        try {
            return L.geoJSON({ type: 'Feature', geometry: feature.geometry, properties: {} }, {
                ..._REFERENCE_LAYER_OPTIONS,
                style: () => ({ ..._REFERENCE_STYLE }),
                pointToLayer: (_f, latlng) => L.circleMarker(latlng, { ..._REFERENCE_LAYER_OPTIONS, radius: 5 })
            });
        } catch (e) {
            console.warn('GeoAssets: reference feature skipped — geometry not drawable.', e);
            return null;
        }
    }

    /** Shows or removes each layer's labels as the zoom crosses its threshold. Labels are bound
     *  only while shown, so a wide view holds no label elements at all. */
    function _syncReferenceLabels(state) {
        const zoom = state.map.getZoom();
        state.referenceLayers.forEach(entry => {
            const show = zoom >= entry.labelMinZoom;
            if (show === entry.labelsShown) return;
            entry.labelsShown = show;
            entry.labelled.forEach(({ layer, label }) => {
                if (!show) { layer.unbindTooltip(); return; }
                // Another organization's text: always a text node, never parsed as HTML.
                const content = document.createElement('span');
                content.textContent = label;
                const isPoint = layer instanceof L.CircleMarker;
                layer.bindTooltip(content, {
                    permanent:   true,
                    interactive: false,
                    direction:   isPoint ? 'right' : 'center',
                    offset:      isPoint ? [6, 0] : [0, 0],
                    className:   'geoassets-reference-label',
                    pane:        _REFERENCE_LABEL_PANE,
                    opacity:     1
                });
                if (!layer.isTooltipOpen()) layer.openTooltip();
            });
        });
    }

    /**
     * Draws `featuresJson` — a JSON array of { id, geometry (RFC 7946), label, assetTypeId,
     * assetTypeName } — as the reference layer `layerId`, replacing what it showed. `options`:
     * { labelMinZoom } — labels show from that zoom up. Entries without a drawable geometry are
     * skipped; anything that is not an array draws an empty layer.
     */
    function setReferenceLayer(divId, layerId, featuresJson, options) {
        const state = _maps[divId];
        if (!state || !layerId) return;

        let features;
        try {
            features = typeof featuresJson === 'string' ? JSON.parse(featuresJson) : featuresJson;
        } catch (e) {
            console.warn('GeoAssets: reference layer ignored — not JSON.', e);
            return;
        }

        clearReferenceLayer(divId, layerId);
        _ensureReferencePanes(state.map);

        const group = L.layerGroup([], { pane: _REFERENCE_PANE, pmIgnore: true, snapIgnore: true });
        const labelled = [];
        (Array.isArray(features) ? features : []).forEach(feature => {
            const shape = _referenceShape(feature);
            if (!shape) return;
            shape.addTo(group);
            const label = typeof feature.label === 'string' ? feature.label.trim() : '';
            const primitive = label ? _firstPrimitive(shape) : null;
            if (primitive) labelled.push({ layer: primitive, label });
        });
        group.addTo(state.map);

        const minZoom = Number.isFinite(options?.labelMinZoom) ? options.labelMinZoom : _REFERENCE_DEFAULT_LABEL_MIN_ZOOM;
        state.referenceLayers.set(layerId, { group, labelled, labelMinZoom: minZoom, labelsShown: false });
        _syncReferenceLabels(state);
    }

    function clearReferenceLayer(divId, layerId) {
        const state = _maps[divId];
        if (!state) return;
        const entry = state.referenceLayers.get(layerId);
        if (!entry) return;
        entry.labelled.forEach(({ layer }) => layer.unbindTooltip());
        entry.group.clearLayers();
        state.map.removeLayer(entry.group);
        state.referenceLayers.delete(layerId);
    }

    /** The current view as [west, south, east, north], or null when the map does not exist. */
    function getViewBounds(divId) {
        const state = _maps[divId];
        if (!state) return null;
        const b = state.map.getBounds();
        return [b.getWest(), b.getSouth(), b.getEast(), b.getNorth()];
    }

    // ─── Per-feature states: public API (XD01-309) ───────────────────────────

    /**
     * Sets the persistent state of `featureIds` to `stateName` — 'default' | 'selected' |
     * 'highlighted' | 'disabled' | 'conflict' | 'pending' | 'done' (case-insensitive). 'default' removes the entry and
     * restores the feature's own style. Ids that are not rendered right now are still recorded
     * and styled when they next render. WebGL mode shows state as colour/alpha only.
     */
    function setFeatureStates(divId, featureIds, stateName) {
        const state = _maps[divId];
        if (!state) return;
        const name = String(stateName || '').toLowerCase();
        if (name !== 'default' && !_STATE_STYLES[name]) {
            console.warn(`GeoAssets: unknown feature state '${stateName}' ignored.`);
            return;
        }
        (featureIds || []).forEach(id => {
            if (name === 'default') state.featureStates.delete(id);
            else state.featureStates.set(id, name);
            _refreshFeatureStyle(state, id);
        });
        _scheduleRedraw(divId); // WebGL only (no-op otherwise); one redraw for the whole call
    }

    /** Empties the persistent registry and restores every feature that had a state. The
     *  transient highlight overlay is owned by its caller and is left in place. */
    function clearFeatureStates(divId) {
        const state = _maps[divId];
        if (!state) return;
        const ids = [...state.featureStates.keys()];
        state.featureStates.clear();
        ids.forEach(id => _refreshFeatureStyle(state, id));
        _scheduleRedraw(divId);
    }

    // ─── Candidate highlight (XD01-152 "Connect to…" picker) ─────────────────
    // A transient overlay drawn on top of whatever persistent state the feature has; clearing it
    // falls back to that persistent state (or the feature's own style). Works in every render
    // mode, WebGL included, because it goes through the same state mechanism.

    function highlightFeature(divId, featureId) {
        const state = _maps[divId];
        if (!state) return;
        state.highlights.add(featureId);
        _refreshFeatureStyle(state, featureId);
        _scheduleRedraw(divId);
    }

    function clearHighlight(divId, featureId) {
        const state = _maps[divId];
        if (!state || !state.highlights.delete(featureId)) return;
        _refreshFeatureStyle(state, featureId);
        _scheduleRedraw(divId);
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

    function _base64ToBytes(base64) {
        const binary = atob(base64);
        const bytes  = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    }

    async function saveBinaryFilePicker(base64Content, suggestedName, mimeType) {
        const extension = '.' + (suggestedName || 'export.bin').split('.').pop();
        const handle = await window.showSaveFilePicker({
            suggestedName: suggestedName || 'export.bin',
            types: [{
                description: 'Export file',
                accept: { [mimeType || 'application/octet-stream']: [extension] }
            }]
        });
        const writable = await handle.createWritable();
        await writable.write(_base64ToBytes(base64Content));
        await writable.close();
    }

    function downloadBinaryFile(base64Content, fileName, mimeType) {
        const blob = new Blob([_base64ToBytes(base64Content)], { type: mimeType || 'application/octet-stream' });
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
        invalidateSize,
        setView,
        registerHandlers,
        enableDraw,
        disableDraw,
        setSnapTargetLayer,
        clearSnapTargetScope,
        setSelectionTool,
        renderFeature,
        renderAllFeatures,
        renderFeatureBatch,
        removeFeature,
        clearAllFeatures,
        beginViewportRender,
        endViewportRender,
        addTileLayer,
        removeTileLayer,
        addWmsLayer,
        removeWmsLayer,
        showTileLayers,
        hideTileLayers,
        setHiddenTileFeatures,
        refreshTileStyle,
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
        clearHighlight,
        localStorageSave,
        localStorageLoad,
        openGeoJsonFilePicker,
        saveGeoJsonFilePicker,
        downloadAsFile,
        saveBinaryFilePicker,
        downloadBinaryFile
    };
})();
