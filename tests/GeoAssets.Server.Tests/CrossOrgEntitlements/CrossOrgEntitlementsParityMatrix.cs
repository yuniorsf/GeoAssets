using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.CrossOrgEntitlementsParity;

/// <summary>
/// XD01-329 — the one client/server test matrix for cross-organization Service Order entitlements. Compiled into both
/// <c>GeoAssets.Server.Tests</c> (the kernel's <c>ServerWorkflowPrincipalFactory</c>, over the identity data of each case)
/// and <c>GeoAssets.Web.Tests</c> (the Web client's <c>RestGeoAuthorizationService</c> and <c>WorkflowPrincipalFactory</c>,
/// over the <c>/me</c> and <c>/me/cross-org-entitlements</c> answers the kernel gives for the same data), each asserting
/// <see cref="Case.Allowed"/>: for the same principal and order, both sides decide the same.
///
/// The scenario is XD01-324's: COELBA grants BIMBO <c>serviceorders:view</c> and <c>serviceorders:execute</c> on its "cut"
/// orders in one município, usable only through a role held on COELBA; a BIMBO technician holds "Contractor C1" on COELBA.
/// </summary>
internal static class CrossOrgEntitlementsParityMatrix
{
    public const string View = "serviceorders:view";
    public const string Execute = "serviceorders:execute";
    public const string Scope = "municipio-123";
    public const string ContractorRole = "Contractor C1";

    public static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000329");
    public static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000329");
    public static readonly Guid TechnicianId = new("7ec40000-0000-4000-8000-000000000329");
    public static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The organizations' names, as the kernel's organization repository holds them.</summary>
    public static IReadOnlyDictionary<Guid, string> OrganizationNames { get; } =
        new Dictionary<Guid, string> { [Coelba] = "COELBA", [Bimbo] = "BIMBO" };

    /// <summary>One row: the identity data the kernel holds, an order, an action and the decision both sides must reach.</summary>
    internal sealed record Case(
        string Name,
        AuthorizationContext Context,
        IReadOnlyList<OrganizationGrant> StoredGrants,
        ServiceOrder Order,
        OrderAction Action,
        bool Allowed)
    {
        /// <summary>
        /// What the kernel's grant repository answers for the technician's organization
        /// (<c>GetActiveGrantsForGranteeAsync</c>): active and unexpired grants held by BIMBO.
        /// </summary>
        public IReadOnlyList<OrganizationGrant> ActiveGrants =>
            [.. StoredGrants.Where(g => g.GranteeOrganizationId == Bimbo && g.IsActive && (g.ExpiresAt is not { } e || e > Now.UtcDateTime))];

        public override string ToString() => Name;
    }

    /// <summary>The rules both sides evaluate with: the two order types of the scenario, the matrix's clock.</summary>
    public static ServiceOrderRules Rules()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "cut", DisplayName = "Cut" });
        registry.Register(new OrderType { Id = "reconnect", DisplayName = "Reconnect" });
        return new ServiceOrderRules(registry, timeProvider: new FakeTimeProvider(Now));
    }

    public static IReadOnlyList<Case> Cases { get; } =
    [
        new("entitled order type: execute", Technician(), [ScopedGrant()], Order(), OrderAction.Execute, Allowed: true),
        new("entitled order type: view", Technician(), [ScopedGrant()], Order(), OrderAction.View, Allowed: true),
        new("entitled order type: an action the grant does not list", Technician(), [ScopedGrant()], Order(), OrderAction.Cancel, Allowed: false),
        new("other order type", Technician(), [ScopedGrant()], Order(orderTypeId: "reconnect"), OrderAction.Execute, Allowed: false),
        new("out-of-scope order", Technician(), [ScopedGrant()], Order(scopeId: "municipio-999"), OrderAction.Execute, Allowed: false),
        new("no scoped role, the permission held at home", Technician(scoped: false, homePermissions: [View, Execute]),
            [ScopedGrant()], Order(), OrderAction.Execute, Allowed: false),
        new("revoked grant", Technician(), [ScopedGrant(active: false)], Order(), OrderAction.Execute, Allowed: false),
        new("no cross-organization entitlement, own organization's order", Technician(scoped: false, homePermissions: [View, Execute]),
            [], Order(owner: Bimbo), OrderAction.Execute, Allowed: true),
    ];

    public static TheoryData<string> CaseNames
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var c in Cases) names.Add(c.Name);
            return names;
        }
    }

    public static Case Get(string name) => Cases.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    private static AuthorizationContext Technician(bool scoped = true, string[]? homePermissions = null) => new()
    {
        User = new AppUser
        {
            Id = TechnicianId,
            Email = "tech@bimbo.example",
            DisplayName = "BIMBO technician",
            OrganizationId = Bimbo,
            CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        },
        Roles = homePermissions is null ? [] : ["Administrator"],
        Claims = [],
        Permissions = homePermissions ?? [],
        ScopedRoles = scoped
            ? new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = [ContractorRole] }
            : new Dictionary<Guid, IReadOnlyList<string>>(),
        ScopedPermissions = scoped
            ? new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = [View, Execute] }
            : new Dictionary<Guid, IReadOnlyList<string>>(),
    };

    private static OrganizationGrant ScopedGrant(bool active = true) => new()
    {
        GranteeOrganizationId = Bimbo,
        ResourceOrganizationId = Coelba,
        ResourceType = "ServiceOrder",
        ScopeId = Scope,
        AllowedActions = [View, Execute],
        RequiresScopedRole = true,
        OrderTypeIds = ["cut"],
        IsActive = active,
        GrantedBy = "coelba-admin",
        GrantedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static ServiceOrder Order(string orderTypeId = "cut", string? scopeId = Scope, Guid? owner = null) => new()
    {
        Id = "ord-329",
        Title = "Cut at the meter",
        OrderTypeId = orderTypeId,
        Status = ServiceOrderStatus.Pending,
        OrganizationId = owner ?? Coelba,
        CreatedBy = "coelba-dispatcher",
        Attributes = scopeId is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = scopeId },
    };
}
