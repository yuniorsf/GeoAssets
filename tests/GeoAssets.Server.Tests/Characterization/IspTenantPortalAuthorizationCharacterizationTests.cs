using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests.Characterization;

/// <summary>
/// Characterization (XD01-355, ADR-001 smell H): pins how the ISP tenant portal authorizes service
/// orders today — through <see cref="OrgResourceRequirement"/> and fixed permission codes rather
/// than through <c>ServiceOrderRules</c>. These facts assert the current, defective behaviour and
/// are meant to be flipped deliberately by XD01-364/XD01-365 (epic XD01-346, P2).
/// </summary>
[Trait("Category", "Characterization")]
public sealed class IspTenantPortalAuthorizationCharacterizationTests
{
    private const string CreatePermission = "serviceorders:create";

    private sealed class FakeAuthorizationService(Guid userOrganizationId, Func<string, bool> grants) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(grants(permissionCode));
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = "tenant@example.com",
                    DisplayName = "Tenant User",
                    CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    OrganizationId = userOrganizationId,
                },
                Roles = [],
                Claims = [],
                Permissions = [],
            });
    }

    private sealed class FakeTenantContext(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId => organizationId;
    }

    private sealed class NoGrantsOrganizationGrantRepository : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<TestServer> BuildPortalServerAsync(
        FakeServiceOrderRepository orders, Guid callerOrganizationId, Func<string, bool> grants)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddGeoAuthorizationPolicyBridge();
                    services.AddSingleton<IGeoAuthorizationService>(new FakeAuthorizationService(callerOrganizationId, grants));
                    services.AddSingleton<IOrganizationGrantRepository, NoGrantsOrganizationGrantRepository>();
                    services.AddSingleton<ITenantContext>(new FakeTenantContext(callerOrganizationId));
                    services.AddSingleton<IServiceOrderRepository>(orders);
                    services.AddSingleton<TimeProvider>(TimeProvider.System);
                    services.AddSingleton<IClandestineAttachmentService, ClandestineAttachmentService>();
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, "tenant-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapIspTenantPortalApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    /// <summary>
    /// Characterization (XD01-355, ADR-001 smell H). Inverted by XD01-364/XD01-365 (epic XD01-346, P2):
    /// disputing a flagged notice will be an action authorized by the <c>ServiceOrderRules</c> chain
    /// for <c>Dispute</c>, not by a create permission.
    /// </summary>
    [Fact]
    public async Task Characterization_UserHoldingOnlyServiceOrdersCreate_CanDisputeFlaggedNotice()
    {
        var orgA = Guid.NewGuid();
        var orders = new FakeServiceOrderRepository();
        using var server = await BuildPortalServerAsync(
            orders, orgA, code => string.Equals(code, CreatePermission, StringComparison.Ordinal));
        var clandestine = new ClandestineAttachmentService(orders, TimeProvider.System);
        var notice = new ServiceOrder
        {
            Id = "notice-1",
            OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId,
            Status = ClandestineAttachmentWorkflowGraph.Active,
            OrganizationId = orgA,
        };
        await orders.AddAsync(notice);
        await clandestine.FlagAsync(notice.Id);
        (await orders.GetByIdAsync(notice.Id))!.Status.Should().Be(ClandestineAttachmentWorkflowGraph.CaboARevelia);
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/isp/tenant-portal/flagged-notices/{notice.Id}/dispute", UriKind.Relative),
            new SubmitDisputeRequest("receipt-digest-abc123"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await orders.GetByIdAsync(notice.Id))!.Status.Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
    }

    /// <summary>
    /// Characterization (XD01-355, ADR-001 smell H). Inverted by XD01-364/XD01-365 (epic XD01-346, P2):
    /// the portal will answer 404 (per XD01-332) so a refusal never confirms that the id exists.
    /// </summary>
    [Fact]
    public async Task Characterization_OtherOrganizationsAttachmentRequest_Returns403InsteadOf404()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var orders = new FakeServiceOrderRepository();
        using var server = await BuildPortalServerAsync(orders, orgB, _ => true);
        await orders.AddAsync(new ServiceOrder
        {
            Id = "request-of-a",
            OrderTypeId = TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            Status = TenantAttachmentRequestWorkflowGraph.Submitted,
            OrganizationId = orgA,
        });
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/api/isp/tenant-portal/attachment-requests/request-of-a", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Characterization (XD01-355, ADR-001 smell H). Inverted by XD01-364/XD01-365 (epic XD01-346, P2):
    /// the workflow API and the portal will agree on who may read the order, because both will go
    /// through <c>ServiceOrderRules</c>.
    /// </summary>
    [Fact]
    public async Task Characterization_OrderDispatchedToOtherOrganization_IsReadableViaWorkflowButRefusedByPortal()
    {
        await using var workflow = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        workflow.OrderTypes.Register(TenantAttachmentRequestOrderTypes.CreateAttachmentRequest());
        await workflow.SeedOrderAsync(
            "request-of-a",
            TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            TenantAttachmentRequestWorkflowGraph.Submitted,
            OrderTargetsApiHost.OrgA);
        await workflow.Orders.AppendDispatchAsync(
            "request-of-a",
            new OrderDispatch(
                OrderTargetsApiHost.OrgB.ToString(),
                DispatchTargetType.Organization,
                "admin",
                OrderTargetsApiHost.Now.UtcDateTime,
                null));
        using var portalServer = await BuildPortalServerAsync(workflow.Orders, OrderTargetsApiHost.OrgB, _ => true);
        using var workflowClient = workflow.ClientFor("outsider");
        using var portalClient = portalServer.CreateClient();

        var viaWorkflow = await workflowClient.GetAsync(new Uri("service-orders/request-of-a", UriKind.Relative));
        var viaPortal = await portalClient.GetAsync(new Uri("/api/isp/tenant-portal/attachment-requests/request-of-a", UriKind.Relative));

        viaWorkflow.StatusCode.Should().Be(HttpStatusCode.OK);
        viaPortal.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
