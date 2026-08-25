using GeoAssets.Core.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Theming;

public static class ThemingServiceExtensions
{
    /// <summary>
    /// Registers the dark/light/system theme service.
    /// Call this from <c>Program.cs</c> before <c>builder.Build()</c>.
    /// </summary>
    public static IServiceCollection AddGeoAssetsTheming(this IServiceCollection services)
    {
        services.AddScoped<BlazorThemeService>();
        services.AddScoped<IThemeService>(sp => sp.GetRequiredService<BlazorThemeService>());

        return services;
    }
}
