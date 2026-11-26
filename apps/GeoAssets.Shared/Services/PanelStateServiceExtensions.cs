using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services.MapTiles;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Shared.Services.ReferenceLayers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Shared.Services;

public static class PanelStateServiceExtensions
{
    /// <summary>
    /// Registers the cross-cutting state services (<see cref="IFeatureSelectionState"/>,
    /// <see cref="ICurrentMapContext"/>, <see cref="IPendingDrawTypeState"/>,
    /// <see cref="IMapSelectionSet"/>, <see cref="MapLayerDisplayService"/>,
    /// <see cref="IMapOrderFocus"/>, <see cref="OrderTargetMapStates"/>, <see cref="ReferenceLayerService"/>,
    /// <see cref="IMapTileAccessProvider"/>) that let panel-type
    /// menu items and map-drawing components become self-sufficient via DI instead of receiving
    /// bespoke parameters from whatever page hosts them (XD01-84).
    /// Called from both <c>Program.cs</c> (Web) and <c>MauiProgram.cs</c>.
    /// </summary>
    public static IServiceCollection AddGeoAssetsPanelState(this IServiceCollection services)
    {
        services.AddScoped<IFeatureSelectionState, FeatureSelectionState>();
        services.AddSingleton<ICurrentMapContext, CurrentMapContext>();
        services.AddScoped<IPendingDrawTypeState, PendingDrawTypeState>();

        // Map multi-selection (XD01-310). A host that can resolve box/fence gestures registers its
        // own IMapSelectionResolver (GeoAssets.Web: RestMapSelectionResolver); one that does not
        // gets the fallback, under which the area tools report "not available" instead of the
        // map workspace failing to resolve its dependencies.
        services.AddScoped<IMapSelectionSet, MapSelectionSet>();
        services.TryAddSingleton<IMapSelectionResolver, UnavailableMapSelectionResolver>();

        // The fence the selection was made with, while it still is (XD01-312): lets an order
        // created from the selection send that fence instead of a list of ids.
        services.AddScoped<MapSelectionFenceMemory>();

        // Layer display and order focus (XD01-311). Both reach the map through the host's own
        // IMapInterop/IMapSelectionInterop registrations; IMapOrderFocus additionally needs the
        // workflow ports (IServiceOrderReader, IServiceOrderTargetReader) a host registers with
        // AddWorkflowRest — it is resolved only by the order UI, never by the map workspace.
        services.AddScoped<MapLayerDisplayService>();
        services.AddScoped<IMapOrderFocus, MapOrderFocusService>();

        // The shown order's targets as map feature states (XD01-313); needs IServiceOrderTargetReader
        // from AddWorkflowRest, and like IMapOrderFocus is resolved only by the order UI.
        services.AddScoped<OrderTargetMapStates>();

        // Another organization's assets as a reference layer (XD01-331); needs ICrossOrgEntitlementsService,
        // IGeoAuthorizationService and TimeProvider from the host. A host that reads reference features registers its
        // own IReferenceFeatureClient (GeoAssets.Web: AddReferenceFeaturesRest); one that does not gets the fallback.
        services.AddScoped<ReferenceLayerService>();
        services.TryAddScoped<IReferenceFeatureClient, UnavailableReferenceFeatureClient>();

        // The token WMS tiles from this app's own server are fetched with (XD01-96). A host with a token source registers
        // its own IMapTileAccessProvider (Web and MAUI: MsalMapTileAccessProvider); one that does not gets the fallback.
        services.TryAddScoped<IMapTileAccessProvider, AnonymousMapTileAccessProvider>();

        return services;
    }
}
