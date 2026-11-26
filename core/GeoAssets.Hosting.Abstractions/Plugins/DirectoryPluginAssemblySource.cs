using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;

namespace GeoAssets.Hosting.Abstractions.Plugins;

/// <summary>
/// The provider plugins dropped into a server host's plugin directory (ADR-002 D2), each loaded into its own
/// <see cref="PluginLoadContext"/>. Two layouts, both under <see cref="RootDirectory"/>:
/// <list type="bullet">
/// <item><b>a folder per plugin</b> — <c>&lt;PluginName&gt;/&lt;PluginName&gt;.dll</c> next to the plugin's
/// <c>&lt;PluginName&gt;.deps.json</c>, its private dependencies and its <c>runtimes/&lt;rid&gt;/native/</c> libraries (the
/// plugin's build output); every folder is a plugin, except <c>runtimes</c> and names starting with <c>.</c>;</item>
/// <item><b>flat</b>, when a <see cref="FlatPluginFilePattern"/> is given — each <c>.dll</c> directly in the root that
/// matches it (the host names its provider assemblies' pattern, e.g. <c>&lt;Prefix&gt;.*.dll</c>), sharing the root
/// with its dependencies.</item>
/// </list>
/// Plugins load by name (ordinal; folders, then flat files) the first time <see cref="GetModuleTypes"/> is called —
/// once; later calls return the same modules. Any broken plugin fails it (D7): the directory missing, a plugin folder
/// without its entry assembly, two plugins with one name, an entry assembly that can't be loaded or whose types can't
/// be read, or one that offers no <see cref="IProviderPluginModule"/>.
/// Not trimming-safe: a server-only source.
/// </summary>
public sealed class DirectoryPluginAssemblySource : IPluginAssemblySource, IPluginLoadContextRegistry
{
    /// <summary>The configuration key naming the plugin directory.</summary>
    public const string DirectoryConfigurationKey = "GeoAssets:ProviderPlugins:Directory";

    /// <summary>The plugin directory when none is configured, relative to the host's base directory.</summary>
    public const string DefaultDirectory = "plugins/providers";

    /// <summary>The configuration key of the flat layout's entry-assembly file pattern (unset: no flat layout).</summary>
    public const string FlatPluginFilePatternConfigurationKey = "GeoAssets:ProviderPlugins:FlatFilePattern";

    private const string NativeAssetsFolder = "runtimes";

    private readonly Lock _gate = new();
    private IReadOnlyList<ProviderPluginModuleDescriptor>? _modules;
    private IReadOnlyList<LoadedProviderPlugin> _plugins = [];

    /// <param name="rootDirectory">The plugin directory; a relative path is relative to <see cref="AppContext.BaseDirectory"/>.</param>
    /// <param name="flatPluginFilePattern">
    /// The flat layout's entry-assembly file pattern (a file name with <c>*</c>/<c>?</c> wildcards, ending in
    /// <c>.dll</c>); <see langword="null"/> or blank for folder-per-plugin only.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="rootDirectory"/> is blank, or <paramref name="flatPluginFilePattern"/> names a folder or not a <c>.dll</c>.
    /// </exception>
    [RequiresUnreferencedCode("Loads provider plugin assemblies from disk and reads all their types.")]
    public DirectoryPluginAssemblySource(string rootDirectory, string? flatPluginFilePattern = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory, AppContext.BaseDirectory);

        if (!string.IsNullOrWhiteSpace(flatPluginFilePattern))
        {
            if (flatPluginFilePattern.IndexOfAny(['/', '\\']) >= 0
                || !flatPluginFilePattern.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The flat provider plugin file pattern '{flatPluginFilePattern}' must be a file name ending in " +
                    ".dll (e.g. 'Prefix.*.dll'), without a folder.",
                    nameof(flatPluginFilePattern));
            }

            FlatPluginFilePattern = flatPluginFilePattern;
        }
    }

    /// <summary>The plugin directory (full path).</summary>
    public string RootDirectory { get; }

    /// <summary>The flat layout's entry-assembly file pattern, or <see langword="null"/> when there is no flat layout.</summary>
    public string? FlatPluginFilePattern { get; }

    /// <inheritdoc />
    public string Name => $"directory:{RootDirectory}";

    /// <inheritdoc />
    public IReadOnlyList<LoadedProviderPlugin> Plugins
    {
        get
        {
            lock (_gate)
                return _plugins;
        }
    }

    /// <inheritdoc />
    public LoadedProviderPlugin? Find(string pluginName)
    {
        ArgumentNullException.ThrowIfNull(pluginName);
        return Plugins.FirstOrDefault(p => string.Equals(p.Name, pluginName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    /// <exception cref="DirectoryNotFoundException"><see cref="RootDirectory"/> doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">A plugin is broken (see the type's remarks).</exception>
    public IReadOnlyList<ProviderPluginModuleDescriptor> GetModuleTypes()
    {
        lock (_gate)
        {
            if (_modules is not null)
                return _modules;

            var plugins = FindEntryAssemblies().Select(Load).ToList();
            _plugins = plugins;
            _modules = [.. plugins.SelectMany(p => p.Modules)];
            return _modules;
        }
    }

    private List<(string Name, string Path)> FindEntryAssemblies()
    {
        if (!Directory.Exists(RootDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The provider plugin directory '{RootDirectory}' does not exist. Create it (an empty directory loads " +
                $"no plugins) or set {DirectoryConfigurationKey} to the directory the plugins are in.");
        }

        var entries = new List<(string Name, string Path)>();
        foreach (var folder in Directory.GetDirectories(RootDirectory).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(folder);
            if (name.StartsWith('.') || string.Equals(name, NativeAssetsFolder, StringComparison.OrdinalIgnoreCase))
                continue;

            var entry = Path.Combine(folder, name + ".dll");
            if (!File.Exists(entry))
            {
                throw new InvalidOperationException(
                    $"Provider plugin folder '{folder}' has no entry assembly '{name}.dll'. A plugin folder holds the " +
                    "plugin's build output and is named after its entry assembly (<PluginName>/<PluginName>.dll); " +
                    "rename the folder or remove it.");
            }

            entries.Add((name, entry));
        }

        if (FlatPluginFilePattern is not null)
        {
            foreach (var file in Directory.GetFiles(RootDirectory, FlatPluginFilePattern, SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
                entries.Add((Path.GetFileNameWithoutExtension(file), file));
        }

        var duplicate = entries
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Two provider plugins are named '{duplicate.Key}' in '{RootDirectory}': " +
                $"{string.Join(" and ", duplicate.Select(e => $"'{e.Path}'"))}. Keep one of them.");
        }

        return entries;
    }

    private static LoadedProviderPlugin Load((string Name, string Path) entry)
    {
        PluginLoadContext context;
        Assembly assembly;
        try
        {
            context = new PluginLoadContext(entry.Path);
            assembly = context.LoadPluginAssembly();
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Cannot load provider plugin '{entry.Name}' from '{entry.Path}': {ex.Message} " +
                "Replace it with a valid .NET assembly built for this runtime, or remove it.",
                ex);
        }

        var modules = ProviderPluginModuleScanner.Scan(assembly);
        if (modules.Count == 0)
        {
            throw new InvalidOperationException(
                $"Provider plugin '{entry.Name}' ('{entry.Path}') offers no provider plugin module: its entry assembly " +
                $"has no public, concrete {nameof(IProviderPluginModule)} class. Add one, or remove the plugin.");
        }

        return new LoadedProviderPlugin(entry.Name, context, assembly, modules);
    }
}
