using FluentAssertions;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Server.Modules.IpBillingBridge;
using GeoAssets.Server.Tests.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
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
}
