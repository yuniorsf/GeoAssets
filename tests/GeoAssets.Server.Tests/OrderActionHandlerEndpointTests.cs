using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <c>POST /service-orders/{id}/actions</c> dispatches to the <see cref="IOrderActionHandler"/> that claims the action
/// (XD01-360) — after the rule chain allowed it — and otherwise appends by default.
/// </summary>
public sealed class OrderActionHandlerEndpointTests
{
    private const string OrderTypeId = "inspection";

    private sealed class FakeAuthorizationService(Guid userId) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AuthorizationContext
            {
                User = new AppUser { Id = userId, Email = "test@example.com", DisplayName = "Test", CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc) },
                Roles = [],
                Claims = [],
                Permissions = [],
            });
    }

    /// <summary>Forwards everything to <paramref name="inner"/>, counting the <see cref="AppendActionAsync"/> calls.</summary>
    private sealed class CountingRepository(IServiceOrderRepository inner) : IServiceOrderRepository
    {
        public int AppendActionCalls { get; private set; }

        public event EventHandler<IServiceOrder>? OrderAdded { add => inner.OrderAdded += value; remove => inner.OrderAdded -= value; }
        public event EventHandler<IServiceOrder>? OrderUpdated { add => inner.OrderUpdated += value; remove => inner.OrderUpdated -= value; }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add => inner.OrderStatusChanged += value; remove => inner.OrderStatusChanged -= value; }
        public event EventHandler<string>? OrderDeleted { add => inner.OrderDeleted += value; remove => inner.OrderDeleted -= value; }

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => inner.GetRootsAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => inner.GetChildrenAsync(parentId, ct);
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => inner.GetParentAsync(childId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => inner.GetByStatusAsync(status, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => inner.GetByAssigneeAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => inner.GetByCreatorAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => inner.GetByOrderTypeAsync(orderTypeId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => inner.GetByDateRangeAsync(from, toExclusive, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => inner.GetDispatchedToAsync(targetId, targetType, ct);
        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => inner.AddAsync(order, ct);
        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default) => inner.UpdateAsync(order, ct);
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => inner.AppendDispatchAsync(orderId, dispatch, ct);
        public Task DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);

        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
        {
            AppendActionCalls++;
            return inner.AppendActionAsync(orderId, entry, ct);
        }
    }

    private sealed class CountingHandler(Func<OrderActionContext, OrderActionOutcome> outcome, OrderActionType claims = OrderActionType.Complete)
        : IOrderActionHandler
    {
        public List<OrderActionContext> Calls { get; } = [];

        public bool CanHandle(OrderType? orderType, OrderActionLog entry) =>
            string.Equals(orderType?.Id, OrderTypeId, StringComparison.Ordinal) && entry.Action == claims;

        public Task<OrderActionOutcome> HandleAsync(OrderActionContext context, CancellationToken cancellationToken)
        {
            Calls.Add(context);
            return Task.FromResult(outcome(context));
        }
    }

    private static async Task<(TestServer Server, IServiceOrderRepository Repo, CountingRepository Spy)> BuildServerAsync(
        Guid callerUserId, params IOrderActionHandler[] handlers)
    {
        var spy = new CountingRepository(new FakeServiceOrderRepository());
        IServiceOrderRepository repository = spy;

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddOrderTypeRegistry();
                    services.AddSingleton<IOrderTypeRepository, FakeOrderTypeRepository>();
                    services.AddSingleton(repository);
                    services.AddServiceOrderRules();
                    services.AddScoped<ServerWorkflowPrincipalFactory>();
                    services.AddSingleton<IGeoAuthorizationService>(new FakeAuthorizationService(callerUserId));
                    services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
                    foreach (var handler in handlers) services.AddSingleton(handler);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapServiceOrdersApi());
                });
            })
            .StartAsync();

        return (host.GetTestServer(), repository, spy);
    }

    private static async Task<ServiceOrder> SeedAsync(IServiceOrderRepository repo, Guid assignee)
    {
        var order = new ServiceOrder
        {
            Title = "Test order",
            OrderTypeId = OrderTypeId,
            CreatedBy = Guid.NewGuid().ToString(),
            AssignedTo = assignee.ToString(),
            Status = ServiceOrderStatus.Draft,
        };
        await repo.AddAsync(order);
        return order;
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, string orderId, Guid caller) =>
        client.PostAsJsonAsync(
            $"/api/workflow/service-orders/{orderId}/actions",
            new OrderActionLog(OrderActionType.Complete, caller.ToString(), new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public async Task Actions_NoHandler_AppendsByDefault()
    {
        var caller = Guid.NewGuid();
        var other = new CountingHandler(_ => OrderActionOutcome.Applied, OrderActionType.Reject);
        var (server, repo, spy) = await BuildServerAsync(caller, other);
        var order = await SeedAsync(repo, caller);
        using var client = server.CreateClient();

        var response = await CompleteAsync(client, order.Id, caller);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        spy.AppendActionCalls.Should().Be(1);
        other.Calls.Should().BeEmpty();
        (await repo.GetByIdAsync(order.Id))!.ActionLog.Should().ContainSingle().Which.Action.Should().Be(OrderActionType.Complete);
    }

    [Fact]
    public async Task Actions_HandlerClaims_IsInvokedExactlyOnce_AndDefaultAppendIsSkipped()
    {
        var caller = Guid.NewGuid();
        var handler = new CountingHandler(_ => OrderActionOutcome.Applied);
        var (server, repo, spy) = await BuildServerAsync(caller, handler);
        var order = await SeedAsync(repo, caller);
        using var client = server.CreateClient();

        var response = await CompleteAsync(client, order.Id, caller);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var call = handler.Calls.Should().ContainSingle().Subject;
        call.Order.Id.Should().Be(order.Id);
        call.OrderType.Id.Should().Be(OrderTypeId);
        call.Entry.Action.Should().Be(OrderActionType.Complete);
        call.Principal.UserId.Should().Be(caller.ToString());
        spy.AppendActionCalls.Should().Be(0);
        (await repo.GetByIdAsync(order.Id))!.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task Actions_HandlerClaims_ButRulesDeny_HandlerIsNotInvoked()
    {
        var caller = Guid.NewGuid();
        var handler = new CountingHandler(_ => OrderActionOutcome.Applied);
        var (server, repo, spy) = await BuildServerAsync(caller, handler);
        var order = await SeedAsync(repo, Guid.NewGuid()); // caller is not the assignee
        using var client = server.CreateClient();

        var response = await CompleteAsync(client, order.Id, caller);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        handler.Calls.Should().BeEmpty();
        spy.AppendActionCalls.Should().Be(0);
    }

    [Fact]
    public async Task Actions_HandlerOutcome_IsMappedToHttp()
    {
        var caller = Guid.NewGuid();
        OrderActionOutcome outcome = OrderActionOutcome.NotFound;
        var handler = new CountingHandler(_ => outcome);
        var (server, repo, _) = await BuildServerAsync(caller, handler);
        var order = await SeedAsync(repo, caller);
        using var client = server.CreateClient();

        outcome = OrderActionOutcome.NotFound;
        (await CompleteAsync(client, order.Id, caller)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        outcome = OrderActionOutcome.ConcurrentModification;
        (await CompleteAsync(client, order.Id, caller)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        outcome = OrderActionOutcome.Denied(RuleEvaluationResult.Deny(OrderActionType.Complete, "handler says no"));
        var denied = await CompleteAsync(client, order.Id, caller);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().Contain("handler says no");

        outcome = OrderActionOutcome.InvalidTransition("Draft", "Done");
        var invalid = await CompleteAsync(client, order.Id, caller);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("from").GetString().Should().Be("Draft");
        json.GetProperty("to").GetString().Should().Be("Done");

        outcome = OrderActionOutcome.Problem(Conflict409, "mod.code", "A title", "A detail");
        var problem = await CompleteAsync(client, order.Id, caller);
        problem.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonDocument.Parse(await problem.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().Should().Be("mod.code");

        outcome = OrderActionOutcome.Applied;
        (await CompleteAsync(client, order.Id, caller)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private const int Conflict409 = 409;
}
