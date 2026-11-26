using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.PostgreSQL;

/// <summary>
/// The PostgreSQL provider's <see cref="IProviderPluginModule"/> (ADR-002 D1, XD01-425): plugin id <c>postgres</c>,
/// on server hosts only — it opens the core asset database, which only <c>GeoAssets.Server</c> may do (Invariant 1).
/// Registers exactly what <see cref="PostgresServiceExtensions.AddGeoAssetsPostgres"/> registers, so a host gets the
/// same services whether it calls the extension or runs this module; the server's spatial pushdowns stay in the
/// kernel's <c>PostgresSpatialModule</c>. Reads no configuration.
/// </summary>
public sealed class PostgresProviderPluginModule : IProviderPluginModule
{
    public string PluginId => "postgres";

    public ProviderHostTargets SupportedHosts => ProviderHostTargets.Server;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGeoAssetsPostgres();
    }
}
