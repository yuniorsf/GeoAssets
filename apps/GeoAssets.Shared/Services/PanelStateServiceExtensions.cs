using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Services;

public static class PanelStateServiceExtensions
{
    /// <summary>
    /// Registers the cross-cutting state services (<see cref="IFeatureSelectionState"/>,
    /// <see cref="ICurrentMapContext"/>) that let panel-type menu items become self-sufficient
    /// via DI instead of receiving bespoke parameters from whatever page hosts them (XD01-84).
    /// Not yet called from <c>Program.cs</c>/<c>MauiProgram.cs</c> — no consumer exists yet.
    /// </summary>
    public static IServiceCollection AddGeoAssetsPanelState(this IServiceCollection services)
    {
        services.AddScoped<IFeatureSelectionState, FeatureSelectionState>();
        services.AddSingleton<ICurrentMapContext, CurrentMapContext>();

        return services;
    }
}
