using System.Text.Json;
using Bunit;
using FluentAssertions;
using GeoAssets.Core.Localization;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Components.ServiceOrders;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.OrderDetail;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.ServiceOrders;

/// <summary>
/// XD01-329 — the Service Order views for a user entitled on another organization: a BIMBO technician holding "Contractor C1"
/// on COELBA, whose grant opens COELBA's "cut" orders. The detail offers Start (Execute) on such an order and not on a COELBA
/// order of another type, and both views name the owning organization; a user with no cross-organization entitlement sees
/// neither change.
/// </summary>
public sealed class CrossOrgServiceOrderViewsTests : BunitContext
{
    private const string View = "serviceorders:view";
    private const string Execute = "serviceorders:execute";

    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000329");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000329");

    private static readonly CrossOrgEntitlementsDto Entitled = new(
        [new CrossOrgGrantEntitlementDto(Coelba, "COELBA", "ServiceOrder", null, ["cut"], [View, Execute], null, RequiresScopedRole: true, null)],
        [new ScopedRoleEntitlementDto(Coelba, "COELBA", ["Contractor C1"], [View, Execute])]);

    private sealed class FixedAuthorizationService(AuthorizationContext context) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(context);
    }

    /// <summary>The technician's principal, as the client's factory builds it over a context carrying <paramref name="entitlements"/>.</summary>
    private static Task<WorkflowPrincipal> TechnicianAsync(CrossOrgEntitlementsDto entitlements) =>
        new WorkflowPrincipalFactory(new FixedAuthorizationService(new AuthorizationContext
        {
            User = new AppUser { Id = Guid.NewGuid(), Email = "t@bimbo.example", DisplayName = "T", OrganizationId = Bimbo, CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) },
            Roles = ["Technician"],
            Claims = [],
            Permissions = [View, Execute],
            OrganizationGrants = entitlements.ToGrants(Bimbo),
            ScopedRoles = entitlements.ToScopedRoles(),
            ScopedPermissions = entitlements.ToScopedPermissions(),
        })).CreateAsync();

    private static ServiceOrder Order(string orderTypeId = "cut", Guid? owner = null, string id = "ord-329") => new()
    {
        Id = id,
        Title = $"Order {id}",
        OrderTypeId = orderTypeId,
        Status = ServiceOrderStatus.Pending,
        CreatedBy = "coelba-dispatcher",
        OrganizationId = owner ?? Coelba,
    };

    private void Arrange(IServiceOrderRepository? repository = null)
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "cut", DisplayName = "Cut" });
        registry.Register(new OrderType { Id = "reconnect", DisplayName = "Reconnect" });

        Services.AddSingleton(registry);
        Services.AddSingleton(new ServiceOrderRules(registry));
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<IServiceOrderRepository>(repository ?? new RecordingOrderRepository());
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddOrderDetailPanels();
    }

    private IRenderedComponent<ServiceOrderDetail> RenderDetail(IServiceOrder order, WorkflowPrincipal principal, CrossOrgEntitlementsDto entitlements) =>
        Render<ServiceOrderDetail>(p => p
            .Add(c => c.Order, order)
            .Add(c => c.Principal, principal)
            .Add(c => c.Entitlements, entitlements));

    private static bool HasButton(IRenderedComponent<ServiceOrderDetail> detail, string key) =>
        detail.FindAll(".order-actions button").Any(b => string.Equals(b.TextContent, key, StringComparison.Ordinal));

    // ── Detail ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Detail_AnEntitledCutOrderOfCoelba_OffersStart_AndNamesTheOwner()
    {
        Arrange();

        var detail = RenderDetail(Order(), await TechnicianAsync(Entitled), Entitled);

        HasButton(detail, "orders.start").Should().BeTrue();
        HasButton(detail, "assets.cancel").Should().BeFalse("the grant does not list serviceorders:cancel");
        detail.Find(".order-owner").TextContent.Should().Contain("orders.ownerOrganization").And.Contain("COELBA");
    }

    [Fact]
    public async Task Detail_ACoelbaOrderOfAnotherType_OffersNoStart()
    {
        Arrange();

        var detail = RenderDetail(Order(orderTypeId: "reconnect"), await TechnicianAsync(Entitled), Entitled);

        HasButton(detail, "orders.start").Should().BeFalse();
    }

    [Fact]
    public async Task Detail_WithoutTheScopedRole_TheGrantOffersNothing_EvenWithTheHomePermission()
    {
        Arrange();
        var withoutRole = Entitled with { ScopedRoles = [] };

        var detail = RenderDetail(Order(), await TechnicianAsync(withoutRole), withoutRole);

        HasButton(detail, "orders.start").Should().BeFalse();
    }

    [Fact]
    public async Task Detail_AnOwnOrganizationOrder_NamesNoOwner()
    {
        Arrange();

        var detail = RenderDetail(Order(owner: Bimbo), await TechnicianAsync(Entitled), Entitled);

        detail.FindAll(".order-owner").Should().BeEmpty();
    }

    [Fact]
    public async Task Detail_AUserWithNoCrossOrganizationEntitlement_SeesNoChange()
    {
        Arrange();

        var detail = RenderDetail(Order(), await TechnicianAsync(CrossOrgEntitlementsDto.None), CrossOrgEntitlementsDto.None);

        detail.FindAll(".order-owner").Should().BeEmpty();
        HasButton(detail, "orders.start").Should().BeFalse();
    }

    // ── List ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_AnOrderOfAnEntitledOrganization_ShowsItsOwner_AnOwnOrderDoesNot()
    {
        var repository = new ListedOrderRepository(Order(id: "coelba-1"), Order(owner: Bimbo, id: "bimbo-1"));
        Arrange(repository);
        var principal = await TechnicianAsync(Entitled);

        var list = Render<ServiceOrderList>(p => p.Add(c => c.Principal, principal).Add(c => c.Entitlements, Entitled));

        var badge = list.Find(".order-owner-badge");
        badge.TextContent.Should().Be("COELBA");
        badge.GetAttribute("title").Should().Be("orders.ownerBadgeTitle");
        list.FindAll(".order-owner-badge").Should().ContainSingle();
    }

    [Fact]
    public async Task List_WithNoEntitlement_ShowsNoOwner()
    {
        Arrange(new ListedOrderRepository(Order(id: "coelba-1")));
        var principal = await TechnicianAsync(CrossOrgEntitlementsDto.None);

        var list = Render<ServiceOrderList>(p => p.Add(c => c.Principal, principal).Add(c => c.Entitlements, CrossOrgEntitlementsDto.None));

        list.FindAll(".order-owner-badge").Should().BeEmpty();
    }

    [Fact]
    public async Task List_RaisesOnReloaded_WhenTheStoreChanges_NotOnItsFirstLoad()
    {
        var repository = new ListedOrderRepository(Order(id: "coelba-1"));
        Arrange(repository);
        var principal = await TechnicianAsync(Entitled);
        var reloads = 0;

        var list = Render<ServiceOrderList>(p => p
            .Add(c => c.Principal, principal)
            .Add(c => c.OnReloaded, EventCallback.Factory.Create(this, () => reloads++)));
        reloads.Should().Be(0);

        repository.RaiseUpdated(Order(id: "coelba-1"));

        list.WaitForAssertion(() => reloads.Should().Be(1));
    }

    // ── Owner helper ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForeignOwnerName_AnUnownedOrder_OrNoEntitlements_NamesNobody()
    {
        var principal = await TechnicianAsync(Entitled);

        ServiceOrderOwner.ForeignOwnerName(Order(owner: Guid.Empty), principal, Entitled).Should().BeNull();
        ServiceOrderOwner.ForeignOwnerName(Order(), principal, entitlements: null).Should().BeNull();
        ServiceOrderOwner.ForeignOwnerName(Order(), principal: null, Entitled).Should().Be("COELBA");
    }

    // ── Strings ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void TheOwnerStrings_ExistInEveryLanguage(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the test runs from inside the repository");

        var path = Path.Combine(directory!.FullName, "apps", "GeoAssets.Shared", "wwwroot", "i18n", $"{language}.json");
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

        strings.Should().ContainKey("orders.ownerOrganization").WhoseValue.Should().NotBeNullOrWhiteSpace();
        strings.Should().ContainKey("orders.ownerBadgeTitle").WhoseValue.Should().Contain("{0}");
    }

    /// <summary>A store holding a fixed set of orders that can announce an update.</summary>
    private sealed class ListedOrderRepository(params IServiceOrder[] orders) : IServiceOrderRepository
    {
        private static NotSupportedException Unused() => new("The list must not call this.");

        public void RaiseUpdated(IServiceOrder order) => OrderUpdated?.Invoke(this, order);

        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IServiceOrder>>(orders);
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([]);

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => throw Unused();
        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default) => throw Unused();
        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => throw Unused();
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => throw Unused();
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => throw Unused();
        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default) => throw Unused();
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => throw Unused();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw Unused();

        public event EventHandler<IServiceOrder>? OrderAdded { add { } remove { } }
        public event EventHandler<IServiceOrder>? OrderUpdated;
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add { } remove { } }
        public event EventHandler<string>? OrderDeleted { add { } remove { } }
    }
}
