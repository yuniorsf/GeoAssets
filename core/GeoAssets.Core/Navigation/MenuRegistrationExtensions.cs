using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Core.Navigation;

public static class MenuRegistrationExtensions
{
    /// <summary>
    /// Discovers every concrete (non-abstract) <see cref="MenuItemBase"/> subclass in the given
    /// assemblies and registers each one in DI — both as its own concrete type (so an item can
    /// take constructor-injected dependencies, the same as an <c>IProviderPlugin</c>
    /// implementation does today) and as <see cref="MenuItemBase"/> — then registers the
    /// <see cref="MenuRegistry"/> singleton that collects them all. Unlike the
    /// <c>IProviderPlugin</c> pattern, no per-item <c>services.AddSingleton&lt;T&gt;()</c> line
    /// is needed — only the assembly itself.
    ///
    /// Call once per relevant assembly from <c>Program.cs</c>/<c>MauiProgram.cs</c> before
    /// <c>.Build()</c> — DI registration cannot happen after the container is built.
    /// </summary>
    public static IServiceCollection AddGeoAssetsNavigation(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        var itemTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && typeof(MenuItemBase).IsAssignableFrom(type));

        foreach (var itemType in itemTypes)
        {
            services.AddSingleton(itemType);
            services.AddSingleton<MenuItemBase>(sp => (MenuItemBase)sp.GetRequiredService(itemType));
        }

        services.AddSingleton<MenuRegistry>();

        return services;
    }
}
