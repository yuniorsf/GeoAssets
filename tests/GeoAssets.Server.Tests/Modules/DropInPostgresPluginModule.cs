using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Server.Tests.Modules;

/// <summary>A drop-in plugin module claiming the kernel's own <c>postgres</c> id (XD01-428).</summary>
internal sealed class DropInPostgresPluginModule : IProviderPluginModule
{
    public string PluginId => "postgres";

    public ProviderHostTargets SupportedHosts => ProviderHostTargets.Server;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
