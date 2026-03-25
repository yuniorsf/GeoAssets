// GeoAssets Draw Interop — Leaflet-Geoman integration
window.GeoAssets = window.GeoAssets || {};

(function (ns) {
    'use strict';

    // ─── Event Handler Registration ────────────────────────────────────────────

    ns.registerHandlers = function (divId, dotNetRef) {
        const state = window.GeoAssets._maps ? window.GeoAssets._maps[divId] : null;
        const mapState = ns._getState(divId);
        if (!mapState) return;

        mapState.dotNetRef = dotNetRef;
        const map = mapState.map;

        // Ensure Geoman controls are present (hidden by default — toolbar drives mode)
        if (!map.pm) {
            console.warn('Leaflet-Geoman not loaded. Draw tools will not work.');
            return;
        }

        map.pm.setGlobalOptions({ snappable: true, snapDistance: 15 });

        // Fired when user finishes drawing a new shape
        map.on('pm:create', ({ layer }) => {
            const geoJson = JSON.stringify(layer.toGeoJSON());
            layer.remove(); // .NET will re-render the feature via renderFeature
            dotNetRef.invokeMethodAsync('OnFeatureDrawnFromJs', geoJson);
        });

        // Fired when an existing shape is edited
        map.on('pm:edit', ({ layer }) => {
            const feature = layer.toGeoJSON();
            const id = feature.id || (layer.feature && layer.feature.id);
            if (id) {
                const geometryJson = JSON.stringify(feature.geometry);
                dotNetRef.invokeMethodAsync('OnFeatureEditedFromJs', id, geometryJson);
            }
        });
    };

    // ─── Draw Mode Control ─────────────────────────────────────────────────────

    ns.enableDraw = function (divId, geometryType) {
        const state = ns._getState(divId);
        if (!state || !state.map.pm) return;

        const modeMap = {
            'Point':      'Marker',
            'LineString': 'Line',
            'Polygon':    'Polygon'
        };

        const mode = modeMap[geometryType];
        if (mode) {
            state.map.pm.enableDraw(mode);
        }
    };

    ns.disableDraw = function (divId) {
        const state = ns._getState(divId);
        if (!state || !state.map.pm) return;
        state.map.pm.disableDraw();
        state.map.pm.disableGlobalEditMode();
    };

    // ─── Internal helper ──────────────────────────────────────────────────────

    ns._getState = function (divId) {
        // Access the maps registry created in mapInterop.js
        // Both files share the same window.GeoAssets namespace
        const registry = ns._registry || (ns._registry = {});
        return registry[divId] || null;
    };

    // Override initializeMap to store state in a shared registry
    const _origInit = ns.initializeMap;
    if (_origInit) {
        ns.initializeMap = function (divId, lat, lon, zoom) {
            _origInit(divId, lat, lon, zoom);
            // The state object is stored by mapInterop.js in maps[divId]
            // We expose it through a getter on the namespace
        };
    }

})(window.GeoAssets);

// Patch: expose the internal maps registry to drawInterop
// This runs after both scripts are loaded
document.addEventListener('DOMContentLoaded', function () {
    // drawInterop accesses state via GeoAssets._getMapState
    window.GeoAssets._getMapState = function (divId) {
        return window.GeoAssets._internalMaps && window.GeoAssets._internalMaps[divId];
    };
});
