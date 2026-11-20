using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.IpBilling.Server;
using GeoAssets.Server.Modules;
using GeoAssets.Server.Tests.Architecture;
using GeoAssets.Workflow.SlaGuard;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// XD01-385 (AC1): the kernel boots with <c>GeoAssets:Modules = [postgres-spatial]</c> only. The município/IP-billing grant bridge
/// left it for GeoAssets.IpBilling.Server: no <c>municipio-grants</c> route, no IP-billing permission contributor or seeded
/// permission, no SlaGuard registration, no reference to the vertical's assemblies, and the retired <c>ip-billing-bridge</c> module id
/// fails the startup with a message naming the known ids.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class KernelWithoutIpBillingBridgeTests
{
    private static readonly string[] GrantApiRoutes =
    [
        "GET /api/identity/organization-grants | authorize, requirement:RolesAuthorizationRequirement",
        "GET /api/identity/organization-grants/me | none",
        "GET /api/identity/organization-grants/sync | policy:organizations:read",
        "POST /api/identity/organization-grants/revoke | authorize, requirement:RolesAuthorizationRequirement",
        "PUT /api/identity/organization-grants | authorize, requirement:RolesAuthorizationRequirement",
    ];

    private static string[] Routes(IServiceProvider services) =>
        [.. RouteInventory.Describe(services.GetRequiredService<EndpointDataSource>().Endpoints)];

    private static async Task<string[]> SnapshotAsync() =>
        [.. (await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Snapshots", "route-inventory.txt"))).Where(line => line.Length > 0)];

    private static IEnumerable<string> Flatten(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            yield return current.Message;
            if (current.InnerException is null) yield break;
        }
    }

    // ── the default composition ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheDefaultHost_ListsOnlyPostgresSpatial()
    {
        var services = await ServerHostFixture.GetServicesAsync();

        services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Select(m => m.Id).Should().Equal("postgres-spatial");
    }

    [Fact]
    public async Task TheDefaultHost_MapsNoMunicipioGrantRoute_NorAnythingOfIpBilling()
    {
        var routes = Routes(await ServerHostFixture.GetServicesAsync());

        routes.Should().NotContain(line => line.Contains("municipio", StringComparison.OrdinalIgnoreCase));
        routes.Should().NotContain(line => line.Contains("ip-billing", StringComparison.OrdinalIgnoreCase), "no route, nor a policy, of the vertical is the kernel's");
    }

    [Fact]
    public async Task TheDefaultHost_RegistersNoIpBillingPermissionContributor()
    {
        var services = await ServerHostFixture.GetServicesAsync();

        services.GetServices<IModulePermissionContributor>().Should().NotContain(c => string.Equals(c.VerticalId, IpBillingPermissionContributor.IpBillingVerticalId, StringComparison.Ordinal));
        services.GetServices<IModulePermissionContributor>().Should().NotContain(c => c is IpBillingPermissionContributor);
    }

    [Fact]
    public async Task TheDefaultHost_SeedsNoIpBillingPermission_TheVerticalPublishesThemItself()
    {
        var services = await ServerHostFixture.GetServicesAsync();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoIdentityDbContext>();

        (await db.Permissions.Where(p => p.Resource == "ip-billing" || p.Code.StartsWith("ip-billing:")).AnyAsync()).Should().BeFalse();
        (await db.Permissions.AnyAsync()).Should().BeTrue("the kernel's own permissions are seeded");
    }

    [Fact]
    public async Task TheDefaultHost_RegistersNoSlaGuardService()
    {
        var services = await ServerHostFixture.GetServicesAsync();
        await using var scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetService<ISlaBreachQueryService>().Should().BeNull("AddSlaBreachReporting left the kernel's Program");
    }

    [Fact]
    public void TheBuiltInModules_AreOnlyPostgresSpatialAndTheKernelHostedNeutralOperator()
    {
        ServerModules.BuiltIns().Select(m => m.Id).Should().Equal("postgres-spatial", "neutral-operator");
    }

    // ── the retired module id ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("postgres-spatial", "ip-billing-bridge")]
    [InlineData("ip-billing-bridge")]
    public async Task ListingTheRetiredIpBillingBridge_FailsTheStartup_NamingTheKnownIds(params string[] modules)
    {
        await using var factory = await ServerHostFixture.CreateUnstartedFactoryAsync(modules);

        var act = () => factory.Services;

        var failure = act.Should().Throw<Exception>().Which;
        var messages = Flatten(failure).ToList();
        messages.Should().Contain(message => message.Contains("Unknown GeoAssets module id 'ip-billing-bridge'", StringComparison.Ordinal));
        messages.Should().Contain(message => message.Contains("Known ids: neutral-operator, postgres-spatial.", StringComparison.Ordinal));
        messages.Should().Contain(message => message.Contains("GeoAssets:Modules", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListingTheRetiredId_EvenNextToValidOnes_NeverStartsTheHostPartly()
    {
        await using var factory = await ServerHostFixture.CreateUnstartedFactoryAsync("postgres-spatial", "neutral-operator", "ip-billing-bridge");

        var act = () => factory.Services;

        act.Should().Throw<Exception>();
    }

    // ── the kernel-only boot (every module entry blank) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task KernelOnly_StillBoots_WithNoModule_AndNoIpBillingContributor()
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();

        factory.Services.GetRequiredService<GeoAssetsModuleCatalog>().Modules.Should().BeEmpty();
        factory.Services.GetServices<IModulePermissionContributor>().Should().BeEmpty();
        Routes(factory.Services).Should().NotContain(line => line.Contains("municipio", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/api/identity/municipio-grants/sync")]
    [InlineData("/api/identity/municipio-grants/revoke")]
    [InlineData("/api/ip-billing/municipio-grants/sync")]
    public async Task KernelOnly_TheMunicipioGrantPaths_AreNotServed(string path)
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { ibgeCode = "3550308" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task KernelOnly_TheGenericGrantApi_IsServiceCallerOnly_AUserTokenGets403()
    {
        // The kernel-only host authenticates every request as a signed-in user whose authorization service permits everything — so a
        // 403 here is the service-caller app role gate itself, not a missing permission.
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        using var client = factory.CreateClient();
        var key = new { granteeOrganizationId = Guid.NewGuid(), resourceOrganizationId = Guid.NewGuid(), resourceType = (string?)null, scopeId = "3550308" };

        using var upsert = await client.PutAsJsonAsync(new Uri("/api/identity/organization-grants", UriKind.Relative), new { key, allowedActions = new[] { "ip-billing:read" }, grantedBy = "x" });
        using var revoke = await client.PostAsJsonAsync(new Uri("/api/identity/organization-grants/revoke", UriKind.Relative), key);
        using var list = await client.GetAsync(new Uri($"/api/identity/organization-grants?granteeOrganizationId={Guid.NewGuid()}&resourceOrganizationId={Guid.NewGuid()}", UriKind.Relative));

        upsert.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── the route snapshot ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCheckedInSnapshot_HasTheGenericGrantRoutes_AndNoMunicipioRoute()
    {
        var snapshot = await SnapshotAsync();

        snapshot.Where(line => line.Contains("organization-grants", StringComparison.Ordinal)).Should().BeEquivalentTo(GrantApiRoutes);
        snapshot.Should().NotContain(line => line.Contains("municipio", StringComparison.OrdinalIgnoreCase));
        snapshot.Should().NotContain(line => line.Contains("ip-billing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheDefaultHost_ServesTheGenericGrantRoutes_AsTheSnapshotHasThem()
    {
        var actual = Routes(await ServerHostFixture.GetServicesAsync());

        actual.Where(line => line.Contains("organization-grants", StringComparison.Ordinal)).Should().BeEquivalentTo(GrantApiRoutes);
    }

    // ── what the kernel references ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheKernelAssembly_ReferencesNeitherSlaGuardNorIpBilling()
    {
        var referenced = typeof(HttpTenantContext).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        referenced.Should().NotContain(name => name.StartsWith("GeoAssets.IpBilling", StringComparison.Ordinal));
        referenced.Should().NotContain("GeoAssets.Workflow.SlaGuard");
    }

    [Fact]
    public void TheKernelsGeoAssetsAssemblyClosure_ReachesNeitherSlaGuardNorIpBilling()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(HttpTenantContext).Assembly]);
        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();
            if (!seen.Add(assembly.GetName().Name!)) continue;

            foreach (var reference in assembly.GetReferencedAssemblies().Where(r => r.Name!.StartsWith("GeoAssets.", StringComparison.Ordinal)))
                pending.Enqueue(Assembly.Load(reference));
        }

        seen.Should().NotContain(name => name.StartsWith("GeoAssets.IpBilling", StringComparison.Ordinal));
        seen.Should().NotContain("GeoAssets.Workflow.SlaGuard");
        seen.Should().Contain("GeoAssets.Core", "the walk really followed the kernel's references");
    }

    [Fact]
    public void TheKernelAssembly_DeclaresNoMunicipioOrIpBillingType()
    {
        var offenders = typeof(HttpTenantContext).Assembly.GetTypes()
            .Select(type => type.FullName ?? type.Name)
            .Where(name => name.Contains("Municipio", StringComparison.OrdinalIgnoreCase) || name.Contains("IpBilling", StringComparison.OrdinalIgnoreCase))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public async Task TheKernelsProjectFile_HasNoSlaGuardOrIpBillingProjectReference()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln"))) directory = directory.Parent;
        directory.Should().NotBeNull();

        var project = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "apps", "GeoAssets.Server", "GeoAssets.Server.csproj"));

        project.Should().NotContain("SlaGuard").And.NotContain("IpBilling");
    }

    [Fact]
    public void TheRatchets_AreAtZero_WithNoViolation()
    {
        KernelReferenceRatchet.Allowlist.Should().BeEmpty();
        VerticalTokenRatchet.Allowlist.Should().BeEmpty();
        VerticalTokenRatchet.DomainReferenceAllowlist.Should().BeEmpty();
        KernelReferenceRatchet.ForbiddenNamespacePrefixes.Should().Contain(["GeoAssets.IpBilling", "GeoAssets.Workflow.SlaGuard"], "the prefixes that keep the vertical out stay forbidden");
        KernelReferenceScanner
            .FindForbiddenReferences(typeof(HttpTenantContext).Assembly.GetTypes().Where(KernelReferenceRatchet.IsKernelType), KernelReferenceRatchet.IsForbidden)
            .Should().BeEmpty();
    }
}
