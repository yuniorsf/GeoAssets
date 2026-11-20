using GeoAssets.Hosting.Abstractions;
using GeoAssets.NeutralOperator.Modules;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// The NeutralOperator module as this host composes it (XD01-382, ADR-001 Phase 7): the
/// <see cref="NeutralOperatorModule"/> that <c>GeoAssets.NeutralOperator.Server</c> hosts on its own, plus the two things
/// only a host that serves its own asset catalog does — applying the asset mutation batches an approval stages to that
/// catalog in process when no broker is configured (XD01-337), and the GIS export over it (XD01-271). Enabled while
/// <c>GeoAssets:Modules</c> lists <see cref="NeutralOperatorModule.ModuleId"/> — today's default; leaving it out is the
/// cut-over to <c>GeoAssets.NeutralOperator.Server</c>, listing it again the rollback (see <c>deploy/neutral-operator/README.md</c>).
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

        // With no broker configured, the asset mutation batches that approval stages are applied to this host's own
        // catalog by an in-process outbox relay (XD01-337); every other message stays pending.
        services.AddServerInProcessAssetMutationApply(configuration);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        _module.MapEndpoints(endpoints);

        // The concessionaire GIS export over this host's catalog (XD01-271) under /api/geoassets.
        endpoints.MapNeutralOperatorGisExportApi();
    }
}
