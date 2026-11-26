using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Plugins;

/// <summary>
/// Where <see cref="ProviderPluginRegistrationExtensions.AddDiscoveredProviderPlugins"/> finds
/// <see cref="IProviderPluginModule"/> types (ADR-002 D2). Each source decides how it gets them —
/// <see cref="StaticAssemblyPluginSource"/> from types the host names (trimming-safe, for WASM/MAUI); a directory
/// source from assemblies it loads into their own load context (servers) — and hands back the module types only:
/// discovery creates, filters, de-duplicates and registers them the same way for every source.
/// </summary>
public interface IPluginAssemblySource
{
    /// <summary>A short name for the source (e.g. <c>"static"</c>), used in discovery's error messages.</summary>
    string Name { get; }

    /// <summary>
    /// The module types this source offers, in a stable order. Called once per discovery. A source that fails to
    /// read an assembly's types should throw — discovery fails the startup rather than run without the module.
    /// </summary>
    IReadOnlyList<ProviderPluginModuleDescriptor> GetModuleTypes();
}
