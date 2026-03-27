// GeoAssets Map Interop — Leaflet.js integration
window.GeoAssets = window.GeoAssets || {};

(function (ns) {
    'use strict';

    const maps    = {};  // divId → { map: L.Map, featureLayers: Map<id, L.Layer>, layerGroups: Map<assetTypeId, L.LayerGroup> }

    // ─── Lifecycle ─────────────────────────────────────────────────────────────

    ns.initializeMap = function (divId, lat, lon, zoom) {
        if (maps[divId]) return; // idempotent

        const map = L.map(divId, {
            zoomControl: true,
            attributionControl: true
        }).setView([lat, lon], zoom);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            maxZoom: 19
        }).addTo(map);

        maps[divId] = {
            map,
            featureLayers: new Map(),
            layerGroups: new Map()
        };
    };

    ns.destroyMap = function (divId) {
        const state = maps[divId];
        if (state) {
            state.map.remove();
            delete maps[divId];
        }
    };

    // ─── Feature Rendering ─────────────────────────────────────────────────────

    ns.renderFeature = function (divId, featureJson) {
        const state = maps[divId];
        if (!state) return;

        const feature = (typeof featureJson === 'string') ? JSON.parse(featureJson) : featureJson;
        const id      = feature.id;
        const props   = feature.properties || {};
        const color   = props.color || '#3388ff';
        const assetTypeId = props.assetTypeId || 'default';

        // Remove stale layer for this id
        if (state.featureLayers.has(id)) {
            state.map.removeLayer(state.featureLayers.get(id));
        }

        const layer = L.geoJSON(feature, {
            style: () => ({
                color,
                weight: 3,
                opacity: 0.9,
                fillOpacity: 0.35
            }),
            pointToLayer: (_f, latlng) => L.circleMarker(latlng, {
                radius: 8,
                color,
                fillColor: color,
                fillOpacity: 0.7,
                weight: 2
            }),
            onEachFeature: (_f, l) => {
                const name = props.name || id;
                l.bindTooltip(name, { permanent: false });
                l.on('click', () => {
                    if (state.dotNetRef) {
                        state.dotNetRef.invokeMethodAsync('OnFeatureClickedFromJs', id);
                    }
                });
            }
        });

        // Add to layer group (for visibility control by assetTypeId)
        let group = state.layerGroups.get(assetTypeId);
        if (!group) {
            group = L.layerGroup().addTo(state.map);
            state.layerGroups.set(assetTypeId, group);
        }
        layer.addTo(group);

        state.featureLayers.set(id, layer);
    };

    ns.renderAllFeatures = function (divId, featuresJson) {
        ns.clearAllFeatures(divId);
        const features = (typeof featuresJson === 'string') ? JSON.parse(featuresJson) : featuresJson;
        (features || []).forEach(f => ns.renderFeature(divId, f));
    };

    ns.removeFeature = function (divId, featureId) {
        const state = maps[divId];
        if (!state) return;
        const layer = state.featureLayers.get(featureId);
        if (layer) {
            state.map.removeLayer(layer);
            // Also remove from any layer group
            state.layerGroups.forEach(g => g.removeLayer(layer));
            state.featureLayers.delete(featureId);
        }
    };

    ns.clearAllFeatures = function (divId) {
        const state = maps[divId];
        if (!state) return;
        state.featureLayers.forEach(layer => state.map.removeLayer(layer));
        state.featureLayers.clear();
        state.layerGroups.forEach(g => g.clearLayers());
        state.layerGroups.clear();
    };

    // ─── Layer Visibility ──────────────────────────────────────────────────────

    ns.setLayerVisibility = function (divId, assetTypeId, visible) {
        const state = maps[divId];
        if (!state) return;
        const group = state.layerGroups.get(assetTypeId);
        if (!group) return;
        if (visible) {
            if (!state.map.hasLayer(group)) state.map.addLayer(group);
        } else {
            state.map.removeLayer(group);
        }
    };

    // ─── View ──────────────────────────────────────────────────────────────────

    ns.fitBounds = function (divId, bbox) {
        const state = maps[divId];
        if (!state || !bbox || bbox.length < 4) return;
        // bbox = [minLon, minLat, maxLon, maxLat]
        state.map.fitBounds([[bbox[1], bbox[0]], [bbox[3], bbox[2]]], { padding: [20, 20] });
    };

    ns.panToFeature = function (divId, featureId) {
        const state = maps[divId];
        if (!state) return;
        const layer = state.featureLayers.get(featureId);
        if (layer) {
            const bounds = layer.getBounds ? layer.getBounds() : null;
            if (bounds) {
                state.map.fitBounds(bounds, { maxZoom: 16 });
            } else if (layer.getLatLng) {
                state.map.setView(layer.getLatLng(), 16);
            }
        }
    };

})(window.GeoAssets);
