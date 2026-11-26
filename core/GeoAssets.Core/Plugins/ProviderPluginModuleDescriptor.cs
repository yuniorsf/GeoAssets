using System.Diagnostics.CodeAnalysis;
using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Plugins;

/// <summary>
/// One <see cref="IProviderPluginModule"/> type an <see cref="IPluginAssemblySource"/> offers. The type carries the
/// trimmer annotation that keeps its public parameterless constructor, so discovery can create it under trimming and
/// AOT without a warning.
/// </summary>
public sealed class ProviderPluginModuleDescriptor
{
    /// <param name="moduleType">A concrete, non-generic class implementing <see cref="IProviderPluginModule"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="moduleType"/> isn't a concrete <see cref="IProviderPluginModule"/> class.</exception>
    public ProviderPluginModuleDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);

        if (!IsModuleType(moduleType))
        {
            throw new ArgumentException(
                $"{moduleType.FullName} ({moduleType.Assembly.GetName().Name}) is not a provider plugin module: " +
                $"it must be a concrete, non-generic class implementing {nameof(IProviderPluginModule)}.",
                nameof(moduleType));
        }

        ModuleType = moduleType;
    }

    /// <summary>The module type; discovery creates it through its public parameterless constructor.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ModuleType { get; }

    /// <summary>Whether <paramref name="type"/> is a concrete, non-generic class implementing <see cref="IProviderPluginModule"/>.</summary>
    public static bool IsModuleType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
            && typeof(IProviderPluginModule).IsAssignableFrom(type);
    }
}
