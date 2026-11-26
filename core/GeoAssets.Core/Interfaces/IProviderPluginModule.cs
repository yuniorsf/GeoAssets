using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// A provider assembly's entry point (ADR-002 D1): run by the host before its
/// <see cref="IServiceProvider"/> is built, it registers the assembly's ports
/// (<see cref="IProviderPlugin"/> and whatever else the provider offers), so they get full
/// constructor injection. Lives in <c>GeoAssets.Core</c> so it crosses plugin load
/// contexts as one shared type (D3).
/// </summary>
public interface IProviderPluginModule
{
    /// <summary>Stable, case-insensitive slug identifying the module (e.g. <c>"rest"</c>).</summary>
    string PluginId { get; }

    /// <summary>The hosts this module can run in; any other host skips it.</summary>
    ProviderHostTargets SupportedHosts { get; }

    /// <summary>Registers the module's services in the host's container.</summary>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
}
