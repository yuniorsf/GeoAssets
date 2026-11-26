using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.PluginModules.Tests;

/// <summary>One spatial provider's module, with what it must declare and the extension it must match.</summary>
internal sealed record ProviderModuleCase(
    Type ModuleType,
    string PluginId,
    ProviderHostTargets SupportedHosts,
    Func<IServiceCollection, IServiceCollection> Extension,
    Type PluginType);
