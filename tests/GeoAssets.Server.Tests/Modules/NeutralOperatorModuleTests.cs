using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Server.Tests.Routing;
using Microsoft.EntityFrameworkCore;
using GeoAssets.Workflow;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Modules;

/// <summary>
/// The ISP neutral-operator vertical as a module of this host (XD01-358): its identity, the order types it adds to the
/// host's registry, and the services it needs the host to provide.
/// </summary>
public sealed class NeutralOperatorModuleTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static IConfiguration ConfigurationWithConnectionString() =>
        Configuration(("ConnectionStrings:GeoAssets", "Host=localhost;Database=unused"));

    [Fact]
    public void Id_IsNeutralOperator()
    {
        new NeutralOperatorModule().Id.Should().Be("neutral-operator").And.Be(NeutralOperatorModule.ModuleId);
    }

    [Fact]
    public void ConfigureServices_RegistersTheVerticalsOrderTypes_InTheHostsRegistry()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();
        var registry = services.BuildServiceProvider().GetRequiredService<OrderTypeRegistry>();
        registry.Find(ClandestineAttachmentOrderTypes.ClandestineNoticeId).Should().BeNull("the module has not run yet");

        new NeutralOperatorModule().ConfigureServices(services, ConfigurationWithConnectionString());

        var after = services.BuildServiceProvider().GetRequiredService<OrderTypeRegistry>();
        after.Should().BeSameAs(registry);
        after.Find(ClandestineAttachmentOrderTypes.ClandestineNoticeId).Should().NotBeNull();
        after.Find(TenantAttachmentRequestOrderTypes.AttachmentRequestId).Should().NotBeNull();
        after.Find(SharedUseOrderTypes.PoleWorkId).Should().NotBeNull();
        after.Find(SharedUseOrderTypes.OccupantNoticeId).Should().NotBeNull();
    }

    [Fact]
    public void ConfigureServices_RegistersTheClandestineAttachmentService()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();

        new NeutralOperatorModule().ConfigureServices(services, ConfigurationWithConnectionString());

        services.Should().ContainSingle(d => d.ServiceType == typeof(IClandestineAttachmentService))
            .Which.ImplementationType.Should().Be<ClandestineAttachmentService>();
    }

    [Fact]
    public void ConfigureServices_PublishesTheSharedUseDescriptor_ToTheOrderTypeRegistry()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();
        services.AddOrderTypeDescriptorRegistry();

        new NeutralOperatorModule().ConfigureServices(services, ConfigurationWithConnectionString());

        using var provider = services.BuildServiceProvider();
        var descriptor = provider.GetRequiredService<IOrderTypeDescriptorRegistry>().GetAll().Should().ContainSingle().Subject;
        descriptor.SameAs(new OrderTypeDescriptor
        {
            VerticalId = "neutral-operator",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            DisplayNameKey = "orderTypes.sharedUsePoleWork",
            ApiBaseRoute = "/api/workflow/service-orders",
            FeatureActions =
            [
                new FeatureActionDescriptor(
                    "occupiable-support", "create", "featureActions.createSharedUseOrder", SharedUseOrderTypes.PoleWorkId, "/api/workflow/service-orders"),
            ],
        }).Should().BeTrue();
    }

    [Fact]
    public void ConfigureServices_KernelHostedModuleWithoutConnectionString_Throws()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();

        var act = () => new KernelHostedNeutralOperatorModule().ConfigureServices(services, Configuration());

        act.Should().Throw<InvalidOperationException>().WithMessage("*neutral-operator*GeoAssets*connection string*");
    }

    [Fact]
    public void ConfigureServices_LibraryModuleWithoutConnectionString_DoesNotRequireOne()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();

        var act = () => new NeutralOperatorModule().ConfigureServices(services, Configuration());

        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureServices_WithoutOrderTypeRegistry_Throws()
    {
        var act = () => new NeutralOperatorModule().ConfigureServices(new ServiceCollection(), ConfigurationWithConnectionString());

        act.Should().Throw<InvalidOperationException>().WithMessage("*OrderTypeRegistry*");
    }

    [Fact]
    public void ConfigureServices_RegistersTheNeutralOperatorPermissionContributor()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();

        new NeutralOperatorModule().ConfigureServices(services, ConfigurationWithConnectionString());

        services.Should().ContainSingle(d => d.ServiceType == typeof(IModulePermissionContributor))
            .Which.ImplementationType.Should().Be<NeutralOperatorPermissionContributor>();
    }

    [Fact]
    public void NeutralOperatorPermissionContributor_PublishesExactlyTheIspTenantBundle_AndNoPermissions()
    {
        var contributor = new NeutralOperatorPermissionContributor();

        contributor.VerticalId.Should().Be("neutral-operator");
        contributor.Permissions.Should().BeEmpty();
        NeutralOperatorPermissionContributor.IspTenantRoleId.Should().Be(new Guid("10000000-0000-0000-0000-000000000005"));
        contributor.RoleBundles.Should().ContainSingle().Which.Should().BeEquivalentTo(new RoleBundleContribution(
            new Guid("10000000-0000-0000-0000-000000000005"),
            "IspTenant",
            "Inquilino ISP: solicitudes y disputas",
            ["serviceorders:read", "serviceorders:create"]));
        new ModulePermissionSet { RoleBundles = [.. contributor.RoleBundles] }.FindProblem().Should().BeNull();
    }

    [Fact]
    public async Task Host_AfterStartup_SeededTheIspTenantRoleWithItsTwoGrants()
    {
        var services = await ServerHostFixture.GetServicesAsync();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoIdentityDbContext>();

        (await db.Roles.AnyAsync(r => r.Id == NeutralOperatorPermissionContributor.IspTenantRoleId && r.Name == "IspTenant")).Should().BeTrue();
        var codes = await db.RolePermissions
            .Where(rp => rp.RoleId == NeutralOperatorPermissionContributor.IspTenantRoleId)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToListAsync();
        codes.Should().BeEquivalentTo(["serviceorders:read", "serviceorders:create"]);
    }
}
