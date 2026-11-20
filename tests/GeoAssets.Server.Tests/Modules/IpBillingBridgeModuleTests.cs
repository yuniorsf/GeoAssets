using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Server.Modules.IpBillingBridge;
using GeoAssets.Server.Tests.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Modules;

/// <summary>
/// The município/IP-billing grant bridge as a module of this host (XD01-359): its identity, the service it registers and
/// the two grant endpoints it maps under the identity prefix.
/// </summary>
public sealed class IpBillingBridgeModuleTests
{
    [Fact]
    public void Id_IsIpBillingBridge()
    {
        new IpBillingBridgeModule().Id.Should().Be("ip-billing-bridge").And.Be(IpBillingBridgeModule.ModuleId);
    }

    [Fact]
    public void ConfigureServices_RegistersMunicipioProvisioningService()
    {
        var services = new ServiceCollection();

        new IpBillingBridgeModule().ConfigureServices(services, new ConfigurationBuilder().Build());

        services.Should().ContainSingle(d => d.ServiceType == typeof(MunicipioProvisioningService))
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task MapEndpoints_MapsMunicipioGrantRoutes_WithTheIpBillingPolicy()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
        builder.Services.AddScoped<MunicipioProvisioningService>();
        await using var app = builder.Build();

        new IpBillingBridgeModule().MapEndpoints(app);

        RouteInventory.Describe(((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints)).Should().BeEquivalentTo(
        [
            "POST /api/identity/municipio-grants/revoke | policy:ip-billing:reference-manage",
            "POST /api/identity/municipio-grants/sync | policy:ip-billing:reference-manage",
        ]);
    }

    private static readonly string[] IpBillingPermissionCodes =
    [
        "ip-billing:read", "ip-billing:upload", "ip-billing:calculate", "ip-billing:validate",
        "ip-billing:ledger-manual", "ip-billing:ledger-recarga", "ip-billing:reference-manage",
    ];

    [Fact]
    public void ConfigureServices_RegistersTheIpBillingPermissionContributor()
    {
        var services = new ServiceCollection();

        new IpBillingBridgeModule().ConfigureServices(services, new ConfigurationBuilder().Build());

        services.Should().ContainSingle(d => d.ServiceType == typeof(IModulePermissionContributor))
            .Which.ImplementationType.Should().Be<IpBillingPermissionContributor>();
    }

    [Fact]
    public void IpBillingPermissionContributor_PublishesExactlyTheSevenIpBillingPermissions_AndNoRoleBundles()
    {
        var contributor = new IpBillingPermissionContributor();

        contributor.VerticalId.Should().Be("ip-billing-bridge");
        contributor.Permissions.Should().Equal(
            new PermissionDefinition("ip-billing:read", "ip-billing", "read", "permissions.ipBilling.read"),
            new PermissionDefinition("ip-billing:upload", "ip-billing", "upload", "permissions.ipBilling.upload"),
            new PermissionDefinition("ip-billing:calculate", "ip-billing", "calculate", "permissions.ipBilling.calculate"),
            new PermissionDefinition("ip-billing:validate", "ip-billing", "validate", "permissions.ipBilling.validate"),
            new PermissionDefinition("ip-billing:ledger-manual", "ip-billing", "ledger-manual", "permissions.ipBilling.ledgerManual"),
            new PermissionDefinition("ip-billing:ledger-recarga", "ip-billing", "ledger-recarga", "permissions.ipBilling.ledgerRecarga"),
            new PermissionDefinition("ip-billing:reference-manage", "ip-billing", "reference-manage", "permissions.ipBilling.referenceManage"));
        contributor.RoleBundles.Should().BeEmpty();
        new ModulePermissionSet { Permissions = [.. contributor.Permissions] }.FindProblem().Should().BeNull();
    }

    [Fact]
    public async Task Host_AfterStartup_SeededTheIpBillingPermissionsIntoTheIdentityDatabase()
    {
        var services = await ServerHostFixture.GetServicesAsync();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoIdentityDbContext>();

        var codes = await db.Permissions.Where(p => p.Resource == "ip-billing").Select(p => p.Code).ToListAsync();

        codes.Should().BeEquivalentTo(IpBillingPermissionCodes);
    }

    [Fact]
    public async Task Host_WithTheIpBillingModuleDisabled_StillStartsWithoutItsContributor()
    {
        var services = await ServerHostFixture.GetIpBillingBridgeDisabledServicesAsync();

        services.GetServices<IModulePermissionContributor>().Should().ContainSingle()
            .Which.Should().BeOfType<GeoAssets.NeutralOperator.Modules.NeutralOperatorPermissionContributor>();
        using var scope = services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<GeoIdentityDbContext>().Roles.AnyAsync()).Should().BeTrue("startup seeded the kernel's roles");
    }
}
