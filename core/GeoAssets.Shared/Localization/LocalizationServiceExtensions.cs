using GeoAssets.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Localization;

public static class LocalizationServiceExtensions
{
    /// <summary>
    /// Registers the JSON localization services.
    /// Call this from <c>Program.cs</c> before <c>builder.Build()</c>.
    ///
    /// <code>
    /// builder.Services.AddGeoAssetsLocalization(opts =>
    /// {
    ///     opts.DefaultCulture    = "es";
    ///     opts.SupportedCultures = ["es", "en", "pt"];
    /// });
    /// </code>
    /// </summary>
    public static IServiceCollection AddGeoAssetsLocalization(
        this IServiceCollection services,
        Action<LocalizationOptions>? configure = null)
    {
        services.Configure<LocalizationOptions>(opts =>
            configure?.Invoke(opts));

        // HttpJsonStringLocalizer uses the default HttpClient registered by the Blazor WASM host,
        // which is pre-configured with the app's base address.
        services.AddScoped<HttpJsonStringLocalizer>();

        services.AddScoped<BlazorCultureService>();
        services.AddScoped<ICultureService>(sp => sp.GetRequiredService<BlazorCultureService>());
        services.AddScoped<IJsonStringLocalizer>(sp => sp.GetRequiredService<HttpJsonStringLocalizer>());

        return services;
    }
}
