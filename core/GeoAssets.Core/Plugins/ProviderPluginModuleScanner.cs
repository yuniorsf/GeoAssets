using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Plugins;

/// <summary>
/// Finds the public <see cref="IProviderPluginModule"/> types of an assembly — for sources that are handed an
/// assembly rather than its module types (<see cref="StaticAssemblyPluginSource.AddAssembly"/>, a directory source).
/// Reading every type of an assembly isn't trimming-safe; a trimmed host (WASM/MAUI) names its module types instead
/// (<see cref="StaticAssemblyPluginSource.Add{TModule}"/>).
/// </summary>
public static class ProviderPluginModuleScanner
{
    internal const string ScanRequiresUnreferencedCode =
        "Scanning an assembly for provider plugin modules reads all its types, which trimming may remove. " +
        "Name the module types with StaticAssemblyPluginSource.Add<TModule>() instead.";

    /// <summary>
    /// Every public, concrete, non-generic <see cref="IProviderPluginModule"/> class in <paramref name="assembly"/>,
    /// ordered by full type name (ordinal).
    /// </summary>
    /// <exception cref="InvalidOperationException">Some of the assembly's types can't be loaded (a <see cref="ReflectionTypeLoadException"/>, kept as the inner exception).</exception>
    [RequiresUnreferencedCode(ScanRequiresUnreferencedCode)]
    public static IReadOnlyList<ProviderPluginModuleDescriptor> Scan(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            throw TypeLoadFailure(assembly, ex);
        }

        var modules = new List<ProviderPluginModuleDescriptor>();
        foreach (var type in types)
        {
            if (type.IsVisible && ProviderPluginModuleDescriptor.IsModuleType(type))
                modules.Add(new ProviderPluginModuleDescriptor(type));
        }

        modules.Sort((a, b) => string.CompareOrdinal(a.ModuleType.FullName, b.ModuleType.FullName));
        return modules;
    }

    private static InvalidOperationException TypeLoadFailure(Assembly assembly, ReflectionTypeLoadException ex)
    {
        var reasons = ex.LoaderExceptions
            .Select(e => e?.Message)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var detail = reasons.Length == 0 ? ex.Message : string.Join(" ", reasons);

        return new InvalidOperationException(
            $"Cannot load the types of provider plugin assembly '{assembly.FullName}': {detail} " +
            "Its provider plugin modules can't be discovered; fix or remove the assembly (often a missing or " +
            "mismatched dependency).",
            ex);
    }
}
