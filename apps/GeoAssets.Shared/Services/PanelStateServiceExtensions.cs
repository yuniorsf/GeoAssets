using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Shared.Services;

public static class PanelStateServiceExtensions
{
    /// <summary>
    /// Registers the cross-cutting state services (<see cref="IFeatureSelectionState"/>,
    /// <see cref="ICurrentMapContext"/>, <see cref="IPendingDrawTypeState"/>,
    /// <see cref="IMapSelectionSet"/>, <see cref="MapLayerDisplayService"/>,
    /// <see cref="IMapOrderFocus"/>) that let panel-type
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

        // Layer display and order focus (XD01-311). Both reach the map through the host's own
        // IMapInterop/IMapSelectionInterop registrations; IMapOrderFocus additionally needs the
        // workflow ports (IServiceOrderReader, IServiceOrderTargetReader) a host registers with
        // AddWorkflowRest — it is resolved only by the order UI, never by the map workspace.
        services.AddScoped<MapLayerDisplayService>();
        services.AddScoped<IMapOrderFocus, MapOrderFocusService>();

        return services;
    }
}
