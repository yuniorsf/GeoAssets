using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Plugins;

/// <summary>
/// The provider plugin modules a host bundles with itself, named at its composition root (ADR-002 D2) — the
/// trimming-safe source for Blazor WebAssembly and MAUI: nothing is loaded by name and no directory is read, and
/// <see cref="Add{TModule}"/> keeps each module's constructor through the trimmer annotation.
/// Offers the modules in the order they were added.
/// </summary>
/// <example>
/// <code>
/// var source = new StaticAssemblyPluginSource()
///     .Add&lt;RestProviderPluginModule&gt;()
///     .Add&lt;WfsProviderPluginModule&gt;();
/// </code>
/// </example>
public sealed class StaticAssemblyPluginSource : IPluginAssemblySource
{
    private readonly List<ProviderPluginModuleDescriptor> _modules = [];

    /// <inheritdoc />
    public string Name => "static";

    /// <summary>Adds the module type <typeparamref name="TModule"/>. Trimming-safe.</summary>
    public StaticAssemblyPluginSource Add<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TModule>()
        where TModule : class, IProviderPluginModule, new() =>
        Add(typeof(TModule));

    /// <summary>Adds the module type <paramref name="moduleType"/>. Trimming-safe.</summary>
    /// <exception cref="ArgumentException"><paramref name="moduleType"/> isn't a concrete <see cref="IProviderPluginModule"/> class.</exception>
    public StaticAssemblyPluginSource Add(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type moduleType)
    {
        _modules.Add(new ProviderPluginModuleDescriptor(moduleType));
        return this;
    }

    /// <summary>
    /// Adds every public module type of <paramref name="assembly"/> (<see cref="ProviderPluginModuleScanner.Scan"/>),
    /// ordered by type name. Not trimming-safe — a trimmed host names its module types with <see cref="Add{TModule}"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Some of the assembly's types can't be loaded.</exception>
    [RequiresUnreferencedCode(ProviderPluginModuleScanner.ScanRequiresUnreferencedCode)]
    public StaticAssemblyPluginSource AddAssembly(Assembly assembly)
    {
        _modules.AddRange(ProviderPluginModuleScanner.Scan(assembly));
        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<ProviderPluginModuleDescriptor> GetModuleTypes() => [.. _modules];
}
