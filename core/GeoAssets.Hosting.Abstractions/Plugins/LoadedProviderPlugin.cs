using System.Reflection;
using GeoAssets.Core.Plugins;

namespace GeoAssets.Hosting.Abstractions.Plugins;

/// <summary>A provider plugin <see cref="DirectoryPluginAssemblySource"/> loaded.</summary>
/// <param name="Name">The plugin's name: its folder's (<c>&lt;Name&gt;/&lt;Name&gt;.dll</c>) or, flat, its file's without <c>.dll</c>.</param>
/// <param name="LoadContext">The plugin's own load context.</param>
/// <param name="Assembly">The plugin's entry assembly, loaded in <paramref name="LoadContext"/>.</param>
/// <param name="Modules">The provider plugin modules the entry assembly offers.</param>
public sealed record LoadedProviderPlugin(
    string Name,
    PluginLoadContext LoadContext,
    Assembly Assembly,
    IReadOnlyList<ProviderPluginModuleDescriptor> Modules);
