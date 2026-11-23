using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.ReferenceLayerOfferParity;

/// <summary>
/// XD01-331 — the one client/server matrix for "may the caller see this organization's reference features". Compiled into
/// both <c>GeoAssets.Server.Tests</c> (the kernel's <c>OrganizationResourceAccess</c> for <c>features:reference</c> on the
/// organization's <c>GeoFeature</c>s — the decision behind <c>GET …/organizations/{id}/reference-features</c>, XD01-330)
/// and <c>GeoAssets.Shared.Tests</c> (the map's <c>ReferenceLayerOffer</c>, over the <c>/me</c> and
/// <c>/me/cross-org-entitlements</c> answers the kernel gives for the same data), each asserting <see cref="Case.Offered"/>:
/// the client offers a toggle exactly when the server would answer <c>200</c>.
///
/// <para>COELBA may grant BIMBO <c>features:reference</c>; the caller is a BIMBO user. Every row asks about COELBA.</para>
/// </summary>
internal static class ReferenceLayerOfferParityMatrix
{
    public const string Reference = "features:reference";
    public const string ContractorRole = "Contractor C1";
    public const string HomeRole = "Field Supervisor";

    public static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000331");
    public static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000331");
    public static readonly Guid Third = new("7a1d0000-0000-4000-8000-000000000331");
    public static readonly DateTimeOffset Now = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The organizations' names, as the kernel's organization repository holds them.</summary>
    public static IReadOnlyDictionary<Guid, string> OrganizationNames { get; } =
        new Dictionary<Guid, string> { [Coelba] = "COELBA", [Bimbo] = "BIMBO", [Third] = "THIRD" };

    /// <summary>One row: the identity data the kernel holds and whether COELBA's reference features open to the caller.</summary>
    internal sealed record Case(string Name, AuthorizationContext Context, IReadOnlyList<OrganizationGrant> StoredGrants, bool Offered)
    {
        /// <summary>The active, unexpired grants held by the caller's organization — what the kernel's repository answers.</summary>
        public IReadOnlyList<OrganizationGrant> ActiveGrants =>
            [.. StoredGrants.Where(g =>
                g.GranteeOrganizationId == Context.User.OrganizationId && g.IsActive && (g.ExpiresAt is not { } e || e > Now.UtcDateTime))];

        /// <summary>What <c>GET /api/identity/me</c> answers: the home half of <see cref="Context"/>.</summary>
        public AuthorizationContext Home => new()
        {
            User = Context.User,
            Roles = Context.Roles,
            Claims = Context.Claims,
            Permissions = Context.Permissions,
        };

        /// <summary>What <c>GET /api/identity/me/cross-org-entitlements</c> answers, built by the kernel's own producer.</summary>
        public CrossOrgEntitlementsDto Entitlements => CrossOrgEntitlementsDto.Create(Context, ActiveGrants, OrganizationNames);

        public override string ToString() => Name;
    }

    public static IReadOnlyList<Case> Cases { get; } =
    [
        new("organization-wide GeoFeature grant, permission at home", Caller(home: [Reference]), [Grant()], Offered: true),
        new("organization-wide grant of every resource type, permission at home", Caller(home: [Reference]), [Grant(resourceType: null)], Offered: true),
        new("a grant for resource type Feature opens nothing", Caller(home: [Reference]), [Grant(resourceType: "Feature")], Offered: false),
        new("a ServiceOrder grant opens nothing", Caller(home: [Reference]), [Grant(resourceType: "ServiceOrder")], Offered: false),
        new("a grant that does not list features:reference", Caller(home: [Reference, "features:read"]), [Grant(actions: ["features:read"])], Offered: false),
        new("a grant limited to order types", Caller(home: [Reference]), [Grant(orderTypeIds: ["cut"])], Offered: false),
        new("organization-wide grant, no home permission", Caller(), [Grant()], Offered: false),
        new("organization-wide grant, the permission only through a role on COELBA", Caller(onCoelba: [Reference]), [Grant()], Offered: false),
        new("scoped-role grant, the permission through a role on COELBA", Caller(onCoelba: [Reference]), [Grant(requiresScopedRole: true)], Offered: true),
        new("scoped-role grant, the permission only at home", Caller(home: [Reference]), [Grant(requiresScopedRole: true)], Offered: false),
        new("organization-wide grant naming a home role the caller holds", Caller(home: [Reference], homeRoles: [HomeRole]),
            [Grant(requiredRole: HomeRole)], Offered: true),
        new("organization-wide grant naming a home role the caller lacks", Caller(home: [Reference]), [Grant(requiredRole: HomeRole)], Offered: false),
        new("organization-wide grant naming a role held only on COELBA", Caller(home: [Reference], onCoelba: [Reference]),
            [Grant(requiredRole: ContractorRole)], Offered: false),
        new("scoped-role grant naming the role held on COELBA", Caller(onCoelba: [Reference]),
            [Grant(requiresScopedRole: true, requiredRole: ContractorRole)], Offered: true),
        new("scoped-role grant naming a role held only at home", Caller(onCoelba: [Reference], homeRoles: [HomeRole]),
            [Grant(requiresScopedRole: true, requiredRole: HomeRole)], Offered: false),
        new("a scoped grant still opens the layer (its scope narrows the answer)", Caller(home: [Reference]), [Grant(scopeId: "municipio-1")], Offered: true),
        new("resource type and action compare ignoring case", Caller(home: [Reference]),
            [Grant(resourceType: "geofeature", actions: ["FEATURES:REFERENCE"])], Offered: true),
        new("revoked grant", Caller(home: [Reference]), [Grant(active: false)], Offered: false),
        new("expired grant", Caller(home: [Reference]), [Grant(expiresAt: Now.UtcDateTime.AddMinutes(-1))], Offered: false),
        new("grant expiring later", Caller(home: [Reference]), [Grant(expiresAt: Now.UtcDateTime.AddDays(1))], Offered: true),
        new("a grant from another organization does not open COELBA", Caller(home: [Reference]), [Grant(resourceOrganization: Third)], Offered: false),
        new("a grant held by another organization does not reach the caller", Caller(home: [Reference]), [Grant(grantee: Third)], Offered: false),
        new("one qualifying grant among others is enough", Caller(home: [Reference]),
            [Grant(resourceType: "Feature"), Grant(requiresScopedRole: true), Grant()], Offered: true),
        new("a caller without an organization", Caller(home: [Reference], withoutOrganization: true), [Grant()], Offered: false),
        new("no grant at all", Caller(home: [Reference]), [], Offered: false),
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

    private static AuthorizationContext Caller(
        string[]? home = null, string[]? onCoelba = null, string[]? homeRoles = null, bool withoutOrganization = false) => new()
    {
        User = new AppUser
        {
            Id = new Guid("7ec40000-0000-4000-8000-000000000331"),
            Email = "tech@bimbo.example",
            DisplayName = "BIMBO technician",
            OrganizationId = withoutOrganization ? null : Bimbo,
            CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        },
        Roles = homeRoles ?? [],
        Claims = [],
        Permissions = home ?? [],
        ScopedRoles = onCoelba is null
            ? new Dictionary<Guid, IReadOnlyList<string>>()
            : new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = [ContractorRole] },
        ScopedPermissions = onCoelba is null
            ? new Dictionary<Guid, IReadOnlyList<string>>()
            : new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = onCoelba },
    };

    private static OrganizationGrant Grant(
        string? resourceType = "GeoFeature",
        string[]? actions = null,
        string[]? orderTypeIds = null,
        bool requiresScopedRole = false,
        string? requiredRole = null,
        string? scopeId = null,
        bool active = true,
        DateTime? expiresAt = null,
        Guid? resourceOrganization = null,
        Guid? grantee = null) => new()
    {
        GranteeOrganizationId = grantee ?? Bimbo,
        ResourceOrganizationId = resourceOrganization ?? Coelba,
        ResourceType = resourceType,
        ScopeId = scopeId,
        AllowedActions = [.. actions ?? [Reference]],
        OrderTypeIds = [.. orderTypeIds ?? []],
        RequiresScopedRole = requiresScopedRole,
        RequiredRole = requiredRole,
        IsActive = active,
        ExpiresAt = expiresAt,
        GrantedBy = "coelba-admin",
        GrantedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
    };
}
