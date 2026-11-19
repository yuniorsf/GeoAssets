using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.Hosting.Abstractions;

namespace GeoAssets.Server.Modules.IpBillingBridge;

/// <summary>
/// The Município/IP-billing grant bridge as a module of this host (XD01-359, ADR-001 Phase 1): the grant-provisioning
/// service and its sync/revoke endpoints, moved out of <c>Program.cs</c> and <c>MapIdentityApi</c> unchanged, so that
/// XD01-385 can move it to <c>GeoAssets.IpBilling.Server</c> as one unit.
/// </summary>
public sealed class IpBillingBridgeModule : IGeoAssetsModule
{
    public const string ModuleId = "ip-billing-bridge";

    public string Id => ModuleId;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        // MunicipioProvisioningService (XD01-177) — grant provisioning for município↔Prefeitura authorization, over the
        // kernel's IOrganizationGrantRepository.
        services.AddScoped<MunicipioProvisioningService>();

        // The vertical's permissions, seeded by the kernel's identity seeding (XD01-377).
        services.AddSingleton<IModulePermissionContributor, IpBillingPermissionContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapMunicipioGrantsApi();
}
