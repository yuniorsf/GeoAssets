using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;
using GeoAssets.Commands.Contracts;

namespace GeoAssets.Commands;

/// <summary>
/// MEF composition root for GIS command plugins.
///
/// Built-in handlers are loaded from the provided <paramref name="builtInAssemblies"/>.
/// External plugins are discovered at runtime from <paramref name="pluginsDirectory"/>
/// by scanning for DLLs matching the pattern <c>GeoAssets.Plugin.*.dll</c>.
///
/// Usage:
/// <code>
///   using var container = new GeoPluginContainer(
///       pluginsDirectory: Path.Combine(AppContext.BaseDirectory, "plugins"),
///       builtInAssemblies: typeof(SomeBuiltInHandler).Assembly);
///
///   var result = await container.ExecuteAsync("nearby-features", context, parameters);
/// </code>
/// </summary>
public sealed class GeoPluginContainer : IDisposable
{
    private readonly CompositionContainer _container;

    [ImportMany]
    public IEnumerable<Lazy<IGeoCommandHandler, IGeoCommandMetadata>> Handlers { get; set; } = [];

    public GeoPluginContainer(string pluginsDirectory, params Assembly[] builtInAssemblies)
    {
        var catalog = new AggregateCatalog();

        foreach (var asm in builtInAssemblies)
            catalog.Catalogs.Add(new AssemblyCatalog(asm));

        if (Directory.Exists(pluginsDirectory))
            catalog.Catalogs.Add(new DirectoryCatalog(pluginsDirectory, "GeoAssets.Plugin.*.dll"));

        _container = new CompositionContainer(catalog);
        _container.ComposeParts(this);
    }

    /// <summary>Returns metadata for all registered commands (built-in + plugins).</summary>
    public IEnumerable<IGeoCommandMetadata> GetAvailableCommands()
        => Handlers.Select(h => h.Metadata).OrderBy(m => m.Category).ThenBy(m => m.Name);

    /// <summary>Executes a command by name.</summary>
    public async Task<GeoCommandResult> ExecuteAsync(
        string commandName,
        GeoCommandContext context,
        IReadOnlyDictionary<string, object>? parameters = null,
        CancellationToken ct = default)
    {
        var export = Handlers.FirstOrDefault(
            h => h.Metadata.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

        if (export is null)
            return GeoCommandResult.Fail($"Command '{commandName}' not found. " +
                $"Available: {string.Join(", ", GetAvailableCommands().Select(m => m.Name))}");

        try
        {
            return await export.Value.ExecuteAsync(
                context,
                parameters ?? new Dictionary<string, object>(),
                ct);
        }
        catch (Exception ex)
        {
            return GeoCommandResult.Fail($"[{commandName}] {ex.Message}");
        }
    }

    public void Dispose() => _container.Dispose();
}
