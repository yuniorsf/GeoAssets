using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Proves XD01-269's tenant-portal endpoints actually enforce the real tenant-isolation
/// boundary (<see cref="OrgResourceAuthorizationHandler"/>, XD01-21) end-to-end over HTTP —
/// not just that the handler is correct in isolation (<see cref="OrgResourceAuthorizationHandlerTests"/>)
/// or that <see cref="ClandestineAttachmentService"/>'s org-scoped query is correct in isolation
/// (<c>ClandestineAttachmentServiceTests</c>, in <c>GeoAssets.Workflow.Tests</c>).
/// </summary>
public class IspTenantPortalRestApiExtensionsTests
{
    private sealed class FakeAuthorizationService(Guid userOrganizationId) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
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

    private sealed class NeverCalledOrganizationGrantRepository : IOrganizationGrantRepository
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

    private static async Task<(TestServer Server, FakeServiceOrderRepository Orders)> BuildServerAsync(Guid callerOrganizationId)
    {
        var orders = new FakeServiceOrderRepository();
        var time = TimeProvider.System;

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
                    services.AddSingleton<IGeoAuthorizationService>(new FakeAuthorizationService(callerOrganizationId));
                    services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
                    services.AddSingleton<ITenantContext>(new FakeTenantContext(callerOrganizationId));
                    services.AddSingleton<IServiceOrderRepository>(orders);
                    services.AddSingleton<TimeProvider>(time);
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

        return (host.GetTestServer(), orders);
    }

    // ── Attachment requests ───────────────────────────────────────────────────

    [Fact]
    public async Task SubmitAttachmentRequest_CreatesOrderScopedToCallersOrganization()
    {
        var orgId = Guid.NewGuid();
        var (server, orders) = await BuildServerAsync(orgId);
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/isp/tenant-portal/attachment-requests", UriKind.Relative),
            new SubmitAttachmentRequestRequest("pole-123", "New fiber attachment"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ServiceOrder>();
        created!.OrganizationId.Should().Be(orgId);
        created.Status.Should().Be(TenantAttachmentRequestWorkflowGraph.Submitted);

        var stored = await orders.GetByIdAsync(created.Id);
        stored.Should().NotBeNull("the request must be a real, trackable record, not fire-and-forget");
    }

    [Fact]
    public async Task ListAttachmentRequests_ReturnsOnlyCallersOwnOrganizationsRequests()
    {
        var myOrg = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        var (server, orders) = await BuildServerAsync(myOrg);
        await orders.AddAsync(new ServiceOrder
        {
            Id = "mine",
            OrderTypeId = TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            Status = TenantAttachmentRequestWorkflowGraph.Submitted,
            OrganizationId = myOrg,
        });
        await orders.AddAsync(new ServiceOrder
        {
            Id = "competitors",
            OrderTypeId = TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            Status = TenantAttachmentRequestWorkflowGraph.Submitted,
            OrganizationId = otherOrg,
        });
        using var client = server.CreateClient();

        var response = await client.GetFromJsonAsync<List<ServiceOrder>>(
            new Uri("/api/isp/tenant-portal/attachment-requests", UriKind.Relative));

        response!.Select(o => o.Id).Should().BeEquivalentTo(["mine"],
            "a tenant must never see a competitor's attachment requests");
    }

    [Fact]
    public async Task GetAttachmentRequest_DifferentOrganization_Returns403()
    {
        var (server, orders) = await BuildServerAsync(Guid.NewGuid());
        var competitorsRequest = new ServiceOrder
        {
            Id = "competitors",
            OrderTypeId = TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            Status = TenantAttachmentRequestWorkflowGraph.Submitted,
            OrganizationId = Guid.NewGuid(),
        };
        await orders.AddAsync(competitorsRequest);
        using var client = server.CreateClient();

        // Direct-ID-manipulation attempt: guessing/incrementing another tenant's order ID.
        var response = await client.GetAsync(new Uri($"/api/isp/tenant-portal/attachment-requests/{competitorsRequest.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAttachmentRequest_OwnOrganization_Returns200()
    {
        var orgId = Guid.NewGuid();
        var (server, orders) = await BuildServerAsync(orgId);
        var myRequest = new ServiceOrder
        {
            Id = "mine",
            OrderTypeId = TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            Status = TenantAttachmentRequestWorkflowGraph.Submitted,
            OrganizationId = orgId,
        };
        await orders.AddAsync(myRequest);
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri($"/api/isp/tenant-portal/attachment-requests/{myRequest.Id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Flagged notices + disputes ────────────────────────────────────────────

    [Fact]
    public async Task ListFlaggedNotices_ReturnsOnlyCallersOwnOrganizationsActiveFlags()
    {
        var myOrg = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        var (server, orders) = await BuildServerAsync(myOrg);
        var clandestine = new ClandestineAttachmentService(orders, TimeProvider.System);

        var mine = new ServiceOrder { Id = "mine", OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId, Status = ClandestineAttachmentWorkflowGraph.Active, OrganizationId = myOrg };
        var competitors = new ServiceOrder { Id = "competitors", OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId, Status = ClandestineAttachmentWorkflowGraph.Active, OrganizationId = otherOrg };
        await orders.AddAsync(mine);
        await orders.AddAsync(competitors);
        await clandestine.FlagAsync(mine.Id);
        await clandestine.FlagAsync(competitors.Id);
        using var client = server.CreateClient();

        var response = await client.GetFromJsonAsync<List<ServiceOrder>>(
            new Uri("/api/isp/tenant-portal/flagged-notices", UriKind.Relative));

        response!.Select(o => o.Id).Should().BeEquivalentTo(["mine"],
            "a tenant must never see a competitor's flagged notices");
    }

    [Fact]
    public async Task SubmitDispute_OwnOrganizationsFlaggedNotice_TransitionsToDisputed()
    {
        var orgId = Guid.NewGuid();
        var (server, orders) = await BuildServerAsync(orgId);
        var clandestine = new ClandestineAttachmentService(orders, TimeProvider.System);
        var notice = new ServiceOrder { Id = "notice-1", OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId, Status = ClandestineAttachmentWorkflowGraph.Active, OrganizationId = orgId };
        await orders.AddAsync(notice);
        await clandestine.FlagAsync(notice.Id);
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/isp/tenant-portal/flagged-notices/{notice.Id}/dispute", UriKind.Relative),
            new SubmitDisputeRequest("receipt-digest-abc123"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reloaded = await orders.GetByIdAsync(notice.Id);
        reloaded!.Status.Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
    }

    [Fact]
    public async Task SubmitDispute_DifferentOrganizationsFlaggedNotice_Returns403_AndDoesNotTransition()
    {
        var (server, orders) = await BuildServerAsync(Guid.NewGuid());
        var clandestine = new ClandestineAttachmentService(orders, TimeProvider.System);
        var competitorsNotice = new ServiceOrder { Id = "competitors-notice", OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId, Status = ClandestineAttachmentWorkflowGraph.Active, OrganizationId = Guid.NewGuid() };
        await orders.AddAsync(competitorsNotice);
        await clandestine.FlagAsync(competitorsNotice.Id);
        using var client = server.CreateClient();

        // Direct-ID-manipulation attempt against a notice that isn't the caller's.
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/isp/tenant-portal/flagged-notices/{competitorsNotice.Id}/dispute", UriKind.Relative),
            new SubmitDisputeRequest("receipt-digest-abc123"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var reloaded = await orders.GetByIdAsync(competitorsNotice.Id);
        reloaded!.Status.Should().Be(ClandestineAttachmentWorkflowGraph.CaboARevelia,
            "a denied dispute attempt must not transition the order");
    }
}
