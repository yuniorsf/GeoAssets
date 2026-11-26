using GeoAssets.Hosting.Abstractions.Plugins;

namespace GeoAssets.Hosting.Abstractions.Tests.Plugins;

/// <summary>
/// The fixture provider plugins (<c>tests/GeoAssets.PluginFixtures.*</c>), built with this project and copied to
/// <c>plugin-fixtures/&lt;Project&gt;/</c> next to it — itself a plugin directory in the folder-per-plugin layout.
/// </summary>
internal static class PluginFixtures
{
    /// <summary>Module <c>fixture-alpha</c> (Server only): private dependency v1, native <c>e_sqlite3</c>.</summary>
    public const string Alpha = "GeoAssets.PluginFixtures.Alpha";

    /// <summary>Module <c>fixture-beta</c> (every host): private dependency v2.</summary>
    public const string Beta = "GeoAssets.PluginFixtures.Beta";

    public const string PrivateDependency = "GeoAssets.PluginFixtures.PrivateDependency";

    public static string Root => Path.Combine(AppContext.BaseDirectory, "plugin-fixtures");

    public static string Folder(string plugin) => Path.Combine(Root, plugin);

    public static string EntryAssembly(string plugin) => Path.Combine(Folder(plugin), plugin + ".dll");

    /// <summary>Calls a public static member of the plugin's module type, which lives in the plugin's load context.</summary>
    public static object? InvokeModuleStatic(PluginLoadContext context, string plugin, string member)
    {
        var shortName = plugin[(plugin.LastIndexOf('.') + 1)..];
        var moduleType = context.LoadPluginAssembly().GetType($"{plugin}.{shortName}FixturePluginModule", throwOnError: true)!;
        var property = moduleType.GetProperty(member);
        return property is not null ? property.GetValue(null) : moduleType.GetMethod(member)!.Invoke(null, null);
    }
}
