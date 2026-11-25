using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// XD01-409 on the NeutralOperator host: a caller of another organization holding a role on the order's organization, under
/// a grant that requires one (XD01-326), may do here what the kernel would let it do — the host reads the caller's scoped
/// roles and its organization's grants from the kernel's <c>/api/identity/me/cross-org-entitlements</c> — and nothing
/// without that role. An entitlements route that fails opens nothing and refuses nobody else.
/// </summary>
public sealed class CrossOrganizationAccessTests
{
    private const string Order = "no-xorg-1";
    private const string View = "serviceorders:view";
    private const string Annotate = "serviceorders:annotate";

    private static CrossOrgGrantEntitlementDto ScopedGrant() => new(
        TestIdentities.OwnerOrganization, "Owner", "ServiceOrder", ScopeId: null, OrderTypeIds: [NeutralOperatorApi.PoleWork],
        AllowedActions: [View, Annotate], RequiredRole: null, RequiresScopedRole: true, ExpiresAt: null);

    private static CrossOrgEntitlementsDto Entitled(bool withScopedRole = true) => new(
        [ScopedGrant()],
        withScopedRole ? [new ScopedRoleEntitlementDto(TestIdentities.OwnerOrganization, "Owner", ["Contractor C1"], [View, Annotate])] : []);

    private static Task<HttpResponseMessage> AnnotateAsync(HttpClient client, string orderId)
    {
        OrderAction annotate = OrderActionType.Annotate;
        return client.PostAsJsonAsync(
            NeutralOperatorApi.Route($"service-orders/{orderId}/actions"),
            new { action = annotate, performedBy = "ignored", performedAt = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc), note = "checked" });
    }

    private static async Task<NeutralOperatorHost> HostWithOrderAsync(Action<FakeKernel>? configureKernel = null)
    {
        var host = await NeutralOperatorHost.StartAsync(configureKernel);
        using var owner = host.ClientFor("owner-admin");
        (await owner.CreateOrderAsync(Order)).StatusCode.Should().Be(HttpStatusCode.Created);
        return host;
    }

    [Fact]
    public async Task AScopedRoleHolderOnTheOwnersOrganization_ReadsAndAnnotatesItsOrder_AsTheKernelWould()
    {
        await using var host = await HostWithOrderAsync(kernel => kernel.Entitlements["contractor"] = Entitled());
        using var contractor = host.ClientFor("contractor");

        var read = await contractor.GetOrderAsync(Order);
        var annotate = await AnnotateAsync(contractor, Order);

        (await read.ReadJsonAsync()).GetProperty("organizationId").GetGuid().Should().Be(TestIdentities.OwnerOrganization);
        annotate.StatusCode.Should().Be(HttpStatusCode.NoContent, await annotate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AScopedRoleHolder_IsStillRefusedAnActionTheGrantDoesNotList()
    {
        await using var host = await HostWithOrderAsync(kernel => kernel.Entitlements["contractor"] = Entitled());
        using var contractor = host.ClientFor("contractor");

        var cancel = await contractor.CancelAsync(Order);

        cancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.ClientFor("owner-admin").StatusOfAsync(Order)).Should().Be(ServiceOrderStatus.Draft);
    }

    [Fact]
    public async Task WithoutTheScopedRole_TheGrantOpensNothing()
    {
        await using var host = await HostWithOrderAsync(kernel => kernel.Entitlements["contractor"] = Entitled(withScopedRole: false));
        using var contractor = host.ClientFor("contractor");

        (await contractor.GetOrderAsync(Order)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnnotateAsync(contractor, Order)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenTheEntitlementsRouteFails_TheGrantOpensNothing_AndTheOwnersRequestsAreServedAsBefore()
    {
        await using var host = await HostWithOrderAsync(kernel =>
        {
            kernel.Entitlements["contractor"] = Entitled();
            kernel.EntitlementsFail = true;
        });
        using var contractor = host.ClientFor("contractor");
        using var owner = host.ClientFor("owner-admin");

        (await contractor.GetOrderAsync(Order)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetOrderAsync(Order)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnnotateAsync(owner, Order)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.CreateOrderAsync("no-xorg-2")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ARequest_ReadsEachKernelIdentityRouteAtMostOnce()
    {
        await using var host = await HostWithOrderAsync(kernel => kernel.Entitlements["contractor"] = Entitled());
        using var contractor = host.ClientFor("contractor");
        var before = host.Kernel.Requests.Count;

        (await AnnotateAsync(contractor, Order)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var identity = host.Kernel.Requests.Skip(before).Where(r => r.Path.StartsWith("/api/identity/", StringComparison.Ordinal)).Select(r => r.Path).ToList();
        identity.Should().OnlyHaveUniqueItems().And.Contain(["/api/identity/me", "/api/identity/me/cross-org-entitlements"]);
        identity.Should().NotContain("/api/identity/organization-grants/me", "the grants come with the entitlements");
    }
}
