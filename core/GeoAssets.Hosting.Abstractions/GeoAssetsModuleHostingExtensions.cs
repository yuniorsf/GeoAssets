using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// The module bootstrap lifecycle of ADR-001 Part 1.2 for a GeoAssets host (XD01-356):
/// <c>AddGeoAssetsModules</c> (discover + configure, before <c>Build()</c>) → <c>SeedGeoAssetsModulesAsync</c> →
/// <c>MapGeoAssetsModules</c> (after the kernel's own routes).
/// </summary>
public static class GeoAssetsModuleHostingExtensions
{
    /// <summary>
    /// Ordered list of enabled module ids. Absent or empty means no modules — the kernel-only boot of invariant I5
    /// (configuration cannot tell an empty JSON array from a missing key, so both mean "none").
    /// </summary>
    public const string ModulesSectionKey = "GeoAssets:Modules";

    /// <summary>
    /// Enables the <paramref name="builtIns"/> listed in <see cref="ModulesSectionKey"/>, in that order, runs each one's
    /// <see cref="IGeoAssetsModule.ConfigureServices"/> and registers the <see cref="GeoAssetsModuleCatalog"/>.
    /// Discovery happens here, before the container is built, because module services must be registered with it.
    /// Throws <see cref="InvalidOperationException"/> on an unknown or duplicate id, or when called twice.
    /// </summary>
    public static WebApplicationBuilder AddGeoAssetsModules(this WebApplicationBuilder builder, params IGeoAssetsModule[] builtIns)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(builtIns);

        if (builder.Services.Any(d => d.ServiceType == typeof(GeoAssetsModuleCatalog)))
        {
            throw new InvalidOperationException("AddGeoAssetsModules has already been called for this host.");
        }

        var enabledIds = builder.Configuration.GetSection(ModulesSectionKey).Get<string[]>() ?? [];
        var modules = GeoAssetsModuleResolver.Resolve(builtIns, enabledIds);

        foreach (var module in modules)
        {
            module.ConfigureServices(builder.Services, builder.Configuration);
        }

        builder.Services.AddSingleton(new GeoAssetsModuleCatalog(modules));
        return builder;
    }

    /// <summary>
    /// Runs every enabled module's <see cref="IModuleSeeder.SeedAsync"/>, if it has one, in the enabled order.
    /// </summary>
    public static async Task SeedGeoAssetsModulesAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        foreach (var module in app.Services.GetRequiredService<GeoAssetsModuleCatalog>().Modules)
        {
            if (module is IModuleSeeder seeder)
            {
                await seeder.SeedAsync(app.Services, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Maps every enabled module's endpoints, in the enabled order, after checking that no two modules map the same
    /// route (<see cref="ModuleRouteCollisionDetector"/>). Throws <see cref="InvalidOperationException"/> on a collision.
    /// </summary>
    public static WebApplication MapGeoAssetsModules(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var mapped = new List<(string ModuleId, ModuleEndpointRouteBuilder Routes)>();
        foreach (var module in app.Services.GetRequiredService<GeoAssetsModuleCatalog>().Modules)
        {
            var routes = new ModuleEndpointRouteBuilder(app);
            module.MapEndpoints(routes);
            mapped.Add((module.Id, routes));
        }

        ModuleRouteCollisionDetector.ThrowIfCollides(
            [.. mapped.Select(m => (m.ModuleId, (IReadOnlyList<Endpoint>)[.. m.Routes.DataSources.SelectMany(ds => ds.Endpoints)]))]);

        var hostDataSources = ((IEndpointRouteBuilder)app).DataSources;
        foreach (var dataSource in mapped.SelectMany(m => m.Routes.DataSources))
        {
            hostDataSources.Add(dataSource);
        }

        return app;
    }
}
