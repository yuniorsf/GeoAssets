using GeoAssets.Hosting.Abstractions;
using GeoAssets.NeutralOperator.Modules;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// The NeutralOperator module as this host composes it (XD01-382, ADR-001 Phase 7): the
/// <see cref="NeutralOperatorModule"/> that <c>GeoAssets.NeutralOperator.Server</c> hosts on its own, plus the one thing
/// only a host that serves its own asset catalog does — the GIS export over it (XD01-271). The asset mutation batches an
/// approval stages are applied by the kernel's own consumer (<c>AddServerAssetMutationApply</c>, XD01-383), registered by
/// the kernel whether or not this module is enabled, over a broker or in process alike. Enabled while
/// <c>GeoAssets:Modules</c> lists <see cref="NeutralOperatorModule.ModuleId"/> — not the default since the XD01-384 cut-over
/// to <c>GeoAssets.NeutralOperator.Server</c>; listing it again (with the gateway's NeutralOperator destination pointed at
/// this host) is the rollback (see <c>deploy/neutral-operator/README.md</c>).
/// </summary>
public sealed class KernelHostedNeutralOperatorModule : IGeoAssetsModule
{
    private readonly NeutralOperatorModule _module = new();

    public string Id => _module.Id;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The module's workflow and occupancy stores live in this host's GeoAssets database: fail at startup without it.
        _ = configuration.GetConnectionString("GeoAssets")
            ?? throw new InvalidOperationException(
                $"The '{Id}' module needs the 'GeoAssets' connection string.");

        _module.ConfigureServices(services, configuration);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        _module.MapEndpoints(endpoints);

        // The concessionaire GIS export over this host's catalog (XD01-271) under /api/geoassets.
        endpoints.MapNeutralOperatorGisExportApi();
    }
}
