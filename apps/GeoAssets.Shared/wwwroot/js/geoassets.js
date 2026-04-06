/**
 * GeoAssets — Unified JavaScript Interop
 * Provides Leaflet map management, draw tools (Leaflet-Geoman),
 * and browser storage utilities for the Blazor app.
 */
window.GeoAssets = (function () {
    'use strict';

    // ─── State registry ──────────────────────────────────────────────────────
    // divId → { map, featureLayers, layerGroups, dotNetRef }

    const _maps = {};

    function getState(divId) {
        return _maps[divId] || null;
    }

    // ─── Map Lifecycle ───────────────────────────────────────────────────────

    function initializeMap(divId, lat, lon, zoom) {
        if (_maps[divId]) return; // idempotent

        const map = L.map(divId, { zoomControl: true }).setView([lat, lon], zoom);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            maxZoom: 19
        }).addTo(map);

        _maps[divId] = {
            map,
            featureLayers: new Map(),  // featureId → L.Layer
            layerGroups:   new Map(),  // assetTypeId → L.LayerGroup
            tileLayers:    new Map(),  // layerId → L.TileLayer
            dotNetRef:     null
        };
    }

    function destroyMap(divId) {
        const state = _maps[divId];
        if (state) {
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

    function renderFeature(divId, featureJson, colorMap) {
        const state = _maps[divId];
        if (!state) return;

        const feature = typeof featureJson === 'string' ? JSON.parse(featureJson) : featureJson;
        const id      = feature.id;
        const props   = feature.properties || {};
        const color   = (colorMap && colorMap[props.assetTypeId]) || '#3388ff';
        const assetTypeId = props.assetTypeId || 'default';

        // Remove stale layer
        if (state.featureLayers.has(id)) {
            const old = state.featureLayers.get(id);
            state.layerGroups.forEach(g => g.removeLayer(old));
            state.map.removeLayer(old);
        }

        const layer = L.geoJSON(feature, {
            style: () => ({ color, weight: 3, opacity: 0.9, fillOpacity: 0.35 }),
            pointToLayer: (_f, latlng) => L.circleMarker(latlng, {
                radius: 8, color, fillColor: color, fillOpacity: 0.7, weight: 2
            }),
            onEachFeature: (_f, l) => {
                const name = props.name || id;
                l.bindTooltip(name, { permanent: false, direction: 'top' });
                l.on('click', () => {
                    state.dotNetRef && state.dotNetRef.invokeMethodAsync('OnFeatureClickedFromJs', id);
                });
                l.on('contextmenu', (e) => {
                    L.DomEvent.stop(e);
                    state.dotNetRef && state.dotNetRef.invokeMethodAsync(
                        'OnFeatureContextMenuFromJs',
                        id,
                        e.originalEvent.clientX,
                        e.originalEvent.clientY
                    );
                });
            }
        });

        // Get or create a LayerGroup for this asset type
        let group = state.layerGroups.get(assetTypeId);
        if (!group) {
            group = L.layerGroup().addTo(state.map);
            state.layerGroups.set(assetTypeId, group);
        }
        layer.addTo(group);
        state.featureLayers.set(id, layer);
    }

    function renderAllFeatures(divId, featuresJson) {
        clearAllFeatures(divId);
        const features = typeof featuresJson === 'string' ? JSON.parse(featuresJson) : featuresJson;
        (features || []).forEach(f => renderFeature(divId, f));
    }

    function renderFeatureBatch(divId, featuresJson, colorMap) {
        const features = typeof featuresJson === 'string' ? JSON.parse(featuresJson) : featuresJson;
        (features || []).forEach(f => renderFeature(divId, f, colorMap));
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
    }

    function clearAllFeatures(divId) {
        const state = _maps[divId];
        if (!state) return;
        state.featureLayers.forEach(l => state.map.removeLayer(l));
        state.featureLayers.clear();
        state.layerGroups.forEach(g => { g.clearLayers(); state.map.removeLayer(g); });
        state.layerGroups.clear();
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
        const group = state.layerGroups.get(assetTypeId);
        if (!group) return;
        visible ? state.map.addLayer(group) : state.map.removeLayer(group);
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
            }
        } else {
            // Try to get center from the geoJSON layer children
            const layers = [];
            layer.eachLayer && layer.eachLayer(l => layers.push(l));
            const first = layers[0];
            if (first && first.getLatLng) {
                state.map.setView(first.getLatLng(), 16);
            }
        }
    }

    // ─── Browser Storage (Web only) ──────────────────────────────────────────

    function localStorageSave(key, value) {
        localStorage.setItem(key, value);
    }

    function localStorageLoad(key) {
        return localStorage.getItem(key);
    }

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
