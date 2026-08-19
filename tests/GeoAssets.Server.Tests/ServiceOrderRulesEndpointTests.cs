using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Proves the real production <c>MapServiceOrdersApi()</c> write endpoints are actually
/// gated by <see cref="GeoAssets.Workflow.Rules.ServiceOrderRules"/> (XD01-16) — not just
/// that the rule engine itself works (already covered by
/// <c>tests/GeoAssets.Workflow.Tests/Rules/ServiceOrderRulesTests.cs</c>).
/// </summary>
public class ServiceOrderRulesEndpointTests
{
    private const string OrderTypeId = "inspection";

    private sealed class FakeAuthorizationService(Guid userId, IReadOnlyList<string> roles) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AuthorizationContext
            {
                User        = new AppUser { Id = userId, Email = "test@example.com", DisplayName = "Test", CreatedAt = DateTime.UtcNow },
                Roles       = roles,
                Claims      = [],
                Permissions = []
            });
    }

    private static async Task<(TestServer Server, IServiceOrderRepository Repo, IOrderTypeRepository OrderTypeRepo)> BuildServerAsync(
        Guid callerUserId, params string[] callerRoles)
    {
        IServiceOrderRepository? repo = null;
        IOrderTypeRepository? orderTypeRepo = null;

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddOrderTypeRegistry();
                    services.AddWorkflowInMemory();
                    services.AddServiceOrderRules();
                    services.AddScoped<ServerWorkflowPrincipalFactory>();
                    services.AddSingleton<IGeoAuthorizationService>(
                        new FakeAuthorizationService(callerUserId, callerRoles));
                    // FakeAuthorizationService's AppUser never sets OrganizationId, so
                    // ServerWorkflowPrincipalFactory (XD01-22) never actually calls this —
                    // registered only because its constructor now requires the dependency.
                    services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapServiceOrdersApi());
                });
            })
            .StartAsync();

        repo = host.Services.GetRequiredService<IServiceOrderRepository>();
        orderTypeRepo = host.Services.GetRequiredService<IOrderTypeRepository>();

        // An order type with a creation policy so create-authorization is actually
        // exercised (an order type with no CreationPolicies is unrestricted by design).
        await orderTypeRepo.AddAsync(new OrderType
        {
            Id          = OrderTypeId,
            DisplayName = "Inspection",
            CreationPolicies = [new OrderCreationPolicy(PolicyKind.Role, "FieldTechnician")]
        });
        await orderTypeRepo.SaveChangesAsync();

        return (host.GetTestServer(), repo, orderTypeRepo);
    }

    private static ServiceOrder NewOrder(string createdBy, string? assignedTo = null) => new()
    {
        Title       = "Test order",
        OrderTypeId = OrderTypeId,
        CreatedBy   = createdBy,
        AssignedTo  = assignedTo,
        Status      = ServiceOrderStatus.Draft,
    };

    // ── POST /service-orders (create) ─────────────────────────────────────────

    [Fact]
    public async Task Create_UserWithoutCreationPolicyRole_Returns403()
    {
        var (server, _, _) = await BuildServerAsync(Guid.NewGuid()); // no roles at all
        using var client = server.CreateClient();
        var body = JsonContent.Create(NewOrder(Guid.NewGuid().ToString()));

        var response = await client.PostAsync("/api/workflow/service-orders", body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_UserWithCreationPolicyRole_Returns201()
    {
        var (server, _, _) = await BuildServerAsync(Guid.NewGuid(), "FieldTechnician");
        using var client = server.CreateClient();
        var body = JsonContent.Create(NewOrder(Guid.NewGuid().ToString()));

        var response = await client.PostAsync("/api/workflow/service-orders", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── PUT /service-orders/{id} (Annotate) ───────────────────────────────────

    [Fact]
    public async Task Update_UnrelatedUser_Returns403()
    {
        // Non-leakage: being authenticated is not enough — must be creator/assignee/role-granted.
        var creatorId = Guid.NewGuid();
        var (server, repo, _) = await BuildServerAsync(Guid.NewGuid()); // caller != creator, no roles
        var order = NewOrder(creatorId.ToString());
        await repo.AddAsync(order);
        using var client = server.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/workflow/service-orders/{order.Id}", order);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_Creator_Returns204()
    {
        var creatorId = Guid.NewGuid();
        var (server, repo, _) = await BuildServerAsync(creatorId);
        var order = NewOrder(creatorId.ToString());
        await repo.AddAsync(order);
        using var client = server.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/workflow/service-orders/{order.Id}", order);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── POST /service-orders/{id}/dispatch (Dispatch) ─────────────────────────

    [Fact]
    public async Task Dispatch_CreatorWithoutSupervisorRole_Returns403()
    {
        // Non-leakage: CreatorRule grants View/Annotate/Cancel only — not Dispatch.
        var creatorId = Guid.NewGuid();
        var (server, repo, _) = await BuildServerAsync(creatorId); // caller == creator, no roles
        var order = NewOrder(creatorId.ToString());
        await repo.AddAsync(order);
        using var client = server.CreateClient();
        var dispatch = new OrderDispatch("target-user", DispatchTargetType.User, creatorId.ToString(), DateTime.UtcNow);

        var response = await client.PostAsJsonAsync($"/api/workflow/service-orders/{order.Id}/dispatch", dispatch);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dispatch_Supervisor_Returns204()
    {
        var creatorId = Guid.NewGuid();
        var (server, repo, _) = await BuildServerAsync(Guid.NewGuid(), "Supervisor");
        var order = NewOrder(creatorId.ToString());
        await repo.AddAsync(order);
        using var client = server.CreateClient();
        var dispatch = new OrderDispatch("target-user", DispatchTargetType.User, "supervisor-id", DateTime.UtcNow);

        var response = await client.PostAsJsonAsync($"/api/workflow/service-orders/{order.Id}/dispatch", dispatch);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── POST /service-orders/{id}/actions (the entry's own Action) ────────────

    [Fact]
    public async Task Action_Complete_ByCreatorOnly_Returns403()
    {
        // Non-leakage: AssigneeRule grants Complete, CreatorRule does not — being the
        // creator must not be enough to complete an order assigned to someone else.
        var creatorId  = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var (server, repo, _) = await BuildServerAsync(creatorId); // caller == creator, not assignee
        var order = NewOrder(creatorId.ToString(), assigneeId.ToString());
        await repo.AddAsync(order);
        using var client = server.CreateClient();
        var entry = new OrderActionLog(OrderActionType.Complete, creatorId.ToString(), DateTime.UtcNow);

        var response = await client.PostAsJsonAsync($"/api/workflow/service-orders/{order.Id}/actions", entry);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Action_Complete_ByAssignee_Returns204()
    {
        var creatorId  = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var (server, repo, _) = await BuildServerAsync(assigneeId); // caller == assignee
        var order = NewOrder(creatorId.ToString(), assigneeId.ToString());
        await repo.AddAsync(order);
        using var client = server.CreateClient();
        var entry = new OrderActionLog(OrderActionType.Complete, assigneeId.ToString(), DateTime.UtcNow);

        var response = await client.PostAsJsonAsync($"/api/workflow/service-orders/{order.Id}/actions", entry);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── DELETE /service-orders/{id} (Cancel) ──────────────────────────────────

    [Fact]
    public async Task Delete_UnrelatedUser_Returns403()
    {
        var creatorId = Guid.NewGuid();
        var (server, repo, _) = await BuildServerAsync(Guid.NewGuid()); // caller != creator, no roles
        var order = NewOrder(creatorId.ToString());
        await repo.AddAsync(order);
        using var client = server.CreateClient();

        var response = await client.DeleteAsync($"/api/workflow/service-orders/{order.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_Creator_Returns204()
    {
        var creatorId = Guid.NewGuid();
        var (server, repo, _) = await BuildServerAsync(creatorId);
        var order = NewOrder(creatorId.ToString());
        await repo.AddAsync(order);
        using var client = server.CreateClient();

        var response = await client.DeleteAsync($"/api/workflow/service-orders/{order.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_Administrator_Returns204()
    {
        var creatorId = Guid.NewGuid();
        var (server, repo, _) = await BuildServerAsync(Guid.NewGuid(), "Administrator");
        var order = NewOrder(creatorId.ToString());
        await repo.AddAsync(order);
        using var client = server.CreateClient();

        var response = await client.DeleteAsync($"/api/workflow/service-orders/{order.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
