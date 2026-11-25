using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static GeoAssets.Server.Tests.OrganizationScopedRoles.ScopedRolesHarness;

namespace GeoAssets.Server.Tests.OrganizationScopedRoles;

/// <summary>
/// XD01-411 — an organization's administrators issue, edit and revoke the grants it gives its contractors
/// (<c>POST|PUT|DELETE {prefix}/organizations/{orgId}/issued-grants</c>), over the production identity stack
/// (<see cref="ScopedRolesHarness"/>). Grants a vertical host provisioned (<see cref="OrganizationGrantSources.Service"/>)
/// stay read-only there, and a vertical's write of an administrator's key takes it over.
/// </summary>
public sealed class IssuedGrantsEndpointTests : IAsyncLifetime, IAsyncDisposable
{
    private const string Read = "serviceorders:read";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // No seeded grant to BIMBO: the acceptance scenario issues it.
    private readonly ScopedRolesHarness _harness = new(contracted: false);

    public Task InitializeAsync() => _harness.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => _harness.DisposeAsync().AsTask();

    ValueTask IAsyncDisposable.DisposeAsync() => _harness.DisposeAsync();

    private DateTime Now => _harness.Time.GetUtcNow().UtcDateTime;

    private static string IssuedGrants(Guid organizationId) => $"/api/identity/organizations/{organizationId}/issued-grants";

    private static IssuedGrantRules Rules(
        string[]? actions = null, bool requiresScopedRole = false, string[]? orderTypes = null,
        string? requiredRole = null, DateTime? expiresAt = null) =>
        new(actions ?? [Read], requiredRole, requiresScopedRole, orderTypes ?? [], expiresAt);

    private static IssuedGrantCreateRequest Create(
        Guid grantee, string? resourceType = "ServiceOrder", IssuedGrantRules? rules = null, string? scopeId = null) =>
        new(grantee, resourceType, scopeId, rules ?? Rules());

    private Task<HttpResponseMessage> IssueAsync(Guid organizationId, IssuedGrantCreateRequest request, Guid asUser) =>
        _harness.SendAsync(HttpMethod.Post, IssuedGrants(organizationId), asUser, request);

    private Task<HttpResponseMessage> EditAsync(Guid organizationId, Guid grantId, IssuedGrantRules rules, Guid asUser) =>
        _harness.SendAsync(HttpMethod.Put, $"{IssuedGrants(organizationId)}/{grantId}", asUser, new IssuedGrantUpdateRequest(rules));

    private Task<HttpResponseMessage> RevokeAsync(Guid organizationId, Guid grantId, Guid asUser) =>
        _harness.SendAsync(HttpMethod.Delete, $"{IssuedGrants(organizationId)}/{grantId}", asUser);

    private async Task<OrganizationGrantDto> IssuedAsync(Guid organizationId, IssuedGrantCreateRequest request, Guid asUser)
    {
        using var response = await IssueAsync(organizationId, request, asUser);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await ReadAsync<OrganizationGrantDto>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(WebJson))!;

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static ServiceOrder CoelbaOrder(string orderTypeId) => new()
    {
        Id = $"coelba-{orderTypeId}",
        Title = orderTypeId,
        OrderTypeId = orderTypeId,
        Status = ServiceOrderStatus.Pending,
        CreatedBy = "coelba",
        OrganizationId = Coelba,
        CreatedAt = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
    };

    private ServiceOrderRules OrderRules()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "cut", DisplayName = "Cut" });
        registry.Register(new OrderType { Id = "reconnect", DisplayName = "Reconnect" });
        return new ServiceOrderRules(registry, timeProvider: new FakeTimeProvider(_harness.Time.GetUtcNow()));
    }

    // ── The acceptance scenario ──────────────────────────────────────────────

    [Fact]
    public async Task CoelbasAdministrator_IssuesACutOnlyScopedRoleGrantToBimbo_AndOnlyBimboUsersWithTheRoleReachOnlyCutOrders()
    {
        // Before the grant BIMBO is no contractor: none of its users can be given a role on COELBA.
        using (var refused = await _harness.AssignAsync(Coelba, BimboTech, ContractorRoleId, CoelbaAdmin))
            (await ProblemCodeAsync(refused)).Should().Be(ScopedRoleAssignmentProblems.NotContracted);

        var grant = await IssuedAsync(Coelba, Create(Bimbo, rules: Rules(requiresScopedRole: true, orderTypes: ["cut"])), CoelbaAdmin);

        grant.GranteeOrganizationId.Should().Be(Bimbo);
        grant.ResourceOrganizationId.Should().Be(Coelba);
        grant.Source.Should().Be(OrganizationGrantSources.Administrator);
        grant.RequiresScopedRole.Should().BeTrue();
        grant.OrderTypeIds.Should().Equal("cut");
        grant.GrantedBy.Should().Be("coelba.admin@coelba.test");
        grant.GrantedAt.Should().Be(Now);

        using (var assigned = await _harness.AssignAsync(Coelba, BimboTech, ContractorRoleId, CoelbaAdmin))
            assigned.StatusCode.Should().Be(HttpStatusCode.Created);

        var rules = OrderRules();
        var withRole = await _harness.PrincipalOfNextRequestAsync(BimboTech);
        var withoutRole = await _harness.PrincipalOfNextRequestAsync(BimboTech2);

        rules.PassesOrganizationGate(withRole, CoelbaOrder("cut")).Should().BeTrue();
        rules.PassesOrganizationGate(withRole, CoelbaOrder("reconnect")).Should().BeFalse("the grant covers only cut orders");
        rules.PassesOrganizationGate(withoutRole, CoelbaOrder("cut")).Should().BeFalse("the grant is usable only through a role held on COELBA");
    }

    // ── Who may write ────────────────────────────────────────────────────────

    [Fact]
    public async Task BimbosAdministrator_CannotIssueEditOrRevokeCoelbasGrants()
    {
        var grant = await IssuedAsync(Coelba, Create(Bimbo), CoelbaAdmin);

        using var issue = await IssueAsync(Coelba, Create(Third), BimboAdmin);
        using var edit = await EditAsync(Coelba, grant.Id, Rules(actions: [Read, "users:read"]), BimboAdmin);
        using var revoke = await RevokeAsync(Coelba, grant.Id, BimboAdmin);

        foreach (var response in new[] { issue, edit, revoke })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ProblemCodeAsync(response)).Should().Be(ScopedRoleAssignmentProblems.Forbidden);
        }

        var row = (await _harness.GrantRowsAsync()).Single(g => g.Id == grant.Id);
        row.IsActive.Should().BeTrue();
        row.AllowedActions.Should().Equal(Read);
        (await _harness.GrantRowsAsync()).Should().NotContain(g => g.GranteeOrganizationId == Third && g.IsActive && g.ExpiresAt == null);
    }

    [Fact]
    public async Task AMemberWithoutTheIssueGrantsPermission_IsRefused()
    {
        using var response = await IssueAsync(Coelba, Create(Bimbo), CoelbaReader);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _harness.GrantRowsAsync()).Should().NotContain(g => g.GranteeOrganizationId == Bimbo);
    }

    [Fact]
    public async Task APlatformAdministrator_IssuesForAnyOrganization()
    {
        var grant = await IssuedAsync(Bimbo, Create(Coelba), PlatformAdmin);

        grant.ResourceOrganizationId.Should().Be(Bimbo);
        grant.GrantedBy.Should().Be("root@platform.test");
    }

    // ── Vertical-provisioned grants ──────────────────────────────────────────

    [Fact]
    public async Task AGrantAVerticalProvisioned_IsReadOnly_AndItsKeyCannotBeIssuedAgain()
    {
        var provisioned = await _harness.UpsertAsServiceAsync(new OrganizationGrantUpsertRequest(
            new OrganizationGrantKey(Bimbo, Coelba, "ServiceOrder", null), [Read], null, null, "ipbilling-sync"));
        var id = provisioned.Grant.Id;

        using var listed = await _harness.SendAsync(HttpMethod.Get, IssuedGrants(Coelba), CoelbaAdmin);
        (await ReadAsync<List<OrganizationGrantDto>>(listed)).Single(g => g.Id == id).Source.Should().Be(OrganizationGrantSources.Service);

        using var edit = await EditAsync(Coelba, id, Rules(actions: [Read, "users:read"]), CoelbaAdmin);
        using var revoke = await RevokeAsync(Coelba, id, CoelbaAdmin);
        using var reissue = await IssueAsync(Coelba, Create(Bimbo), CoelbaAdmin);

        foreach (var response in new[] { edit, revoke, reissue })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(response)).Should().Be(IssuedGrantProblems.ServiceOwned);
        }

        var row = (await _harness.GrantRowsAsync()).Single(g => g.Id == id);
        row.IsActive.Should().BeTrue();
        row.AllowedActions.Should().Equal(Read);
        row.Source.Should().Be(OrganizationGrantSources.Service);
    }

    [Fact]
    public async Task AVerticalWritingAnAdministratorsKey_TakesTheGrantOver_AndTheAdministratorCanNoLongerEditIt()
    {
        var issued = await IssuedAsync(Coelba, Create(Bimbo), CoelbaAdmin);

        var upsert = await _harness.UpsertAsServiceAsync(new OrganizationGrantUpsertRequest(
            new OrganizationGrantKey(Bimbo, Coelba, "ServiceOrder", null), [Read], null, null, "ipbilling-sync"));

        upsert.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Updated, "the rules match, but the owner changes");
        upsert.Grant.Id.Should().Be(issued.Id);
        upsert.Grant.Source.Should().Be(OrganizationGrantSources.Service);

        using var edit = await EditAsync(Coelba, issued.Id, Rules(requiresScopedRole: true), CoelbaAdmin);
        (await ProblemCodeAsync(edit)).Should().Be(IssuedGrantProblems.ServiceOwned);

        var again = await _harness.UpsertAsServiceAsync(new OrganizationGrantUpsertRequest(
            new OrganizationGrantKey(Bimbo, Coelba, "ServiceOrder", null), [Read], null, null, "ipbilling-sync"));
        again.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Unchanged);
    }

    // ── Issue ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IssuingAKeyThatAlreadyHasAnAdministratorsGrant_IsAConflict_EditItInstead()
    {
        await IssuedAsync(Coelba, Create(Bimbo), CoelbaAdmin);

        using var response = await IssueAsync(Coelba, Create(Bimbo, rules: Rules(requiresScopedRole: true)), CoelbaAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(response)).Should().Be(IssuedGrantProblems.Exists);
        (await _harness.GrantRowsAsync()).Count(g => g.GranteeOrganizationId == Bimbo).Should().Be(1);
    }

    [Fact]
    public async Task AnotherScopeOrResourceType_IsAnotherGrant()
    {
        await IssuedAsync(Coelba, Create(Bimbo), CoelbaAdmin);
        await IssuedAsync(Coelba, Create(Bimbo, scopeId: "2927408"), CoelbaAdmin);
        await IssuedAsync(Coelba, Create(Bimbo, resourceType: null), CoelbaAdmin);

        (await _harness.GrantRowsAsync()).Count(g => g.GranteeOrganizationId == Bimbo && g.IsActive).Should().Be(3);
    }

    [Fact]
    public async Task AnExpiredGrantStillFlaggedActive_IsRetired_SoTheKeyTakesTheNewOne()
    {
        var old = await IssuedAsync(Coelba, Create(Bimbo, rules: Rules(expiresAt: Now.AddDays(1))), CoelbaAdmin);
        _harness.Time.Advance(TimeSpan.FromDays(2));

        var renewed = await IssuedAsync(Coelba, Create(Bimbo), CoelbaAdmin);

        var rows = await _harness.GrantRowsAsync();
        rows.Single(g => g.Id == old.Id).IsActive.Should().BeFalse();
        rows.Single(g => g.Id == renewed.Id).IsActive.Should().BeTrue();
    }

    public static TheoryData<string, IssuedGrantCreateRequest> InvalidRequests => new()
    {
        { "unknown permission", Create(Bimbo, rules: Rules(actions: ["serviceorders:fly"])) },
        { "no action", Create(Bimbo, rules: Rules(actions: [])) },
        { "unknown role", Create(Bimbo, rules: Rules(requiredRole: "Astronaut")) },
        { "order types on features", Create(Bimbo, resourceType: "GeoFeature", rules: Rules(orderTypes: ["cut"])) },
        { "own organization", Create(Coelba) },
        { "unknown organization", Create(Guid.NewGuid()) },
        { "blank scope", Create(Bimbo, scopeId: " ") },
        { "past expiry", Create(Bimbo, rules: Rules(expiresAt: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc))) },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task AnInvalidGrant_IsRefused_AndNothingIsStored(string because, IssuedGrantCreateRequest request)
    {
        using var response = await IssueAsync(Coelba, request, CoelbaAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because);
        (await ProblemCodeAsync(response)).Should().Be(IssuedGrantProblems.Invalid, because);
        (await _harness.GrantRowsAsync()).Should().NotContain(g => g.ResourceOrganizationId == Coelba && g.IsActive && g.ExpiresAt == null, because);
    }

    [Fact]
    public async Task AnExpiryWithoutAZone_IsTakenAsUtc()
    {
        var grant = await IssuedAsync(
            Coelba, Create(Bimbo, rules: Rules(expiresAt: new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Unspecified))), CoelbaAdmin);

        grant.ExpiresAt.Should().Be(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
    }

    // ── Edit ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Editing_ReplacesTheRules_AndKeepsTheKeyAndWhoIssuedIt()
    {
        var issued = await IssuedAsync(Coelba, Create(Bimbo), CoelbaAdmin);
        _harness.Time.Advance(TimeSpan.FromHours(1));

        using var response = await EditAsync(
            Coelba, issued.Id, Rules(actions: [Read, "users:read"], requiresScopedRole: true, orderTypes: ["cut"],
                requiredRole: ContractorRole, expiresAt: Now.AddDays(30)), PlatformAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var edited = await ReadAsync<OrganizationGrantDto>(response);
        edited.Id.Should().Be(issued.Id);
        edited.AllowedActions.Should().BeEquivalentTo([Read, "users:read"]);
        edited.RequiresScopedRole.Should().BeTrue();
        edited.OrderTypeIds.Should().Equal("cut");
        edited.RequiredRole.Should().Be(ContractorRole);
        edited.ExpiresAt.Should().Be(Now.AddDays(30));
        edited.GranteeOrganizationId.Should().Be(Bimbo);
        edited.ResourceType.Should().Be("ServiceOrder");
        edited.GrantedBy.Should().Be(issued.GrantedBy);
        edited.GrantedAt.Should().Be(issued.GrantedAt);
        edited.Source.Should().Be(OrganizationGrantSources.Administrator);
    }

    [Fact]
    public async Task EditingAGrantAnotherOrganizationIssued_IsNotFound()
    {
        var bimbos = await IssuedAsync(Bimbo, Create(Coelba), PlatformAdmin);

        using var edit = await EditAsync(Coelba, bimbos.Id, Rules(), CoelbaAdmin);
        using var revoke = await RevokeAsync(Coelba, bimbos.Id, CoelbaAdmin);

        foreach (var response in new[] { edit, revoke })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ProblemCodeAsync(response)).Should().Be(IssuedGrantProblems.NotFound);
        }
    }

    [Fact]
    public async Task EditingWithOrderTypesAGrantThatIsNotForServiceOrders_IsInvalid()
    {
        var issued = await IssuedAsync(Coelba, Create(Bimbo, resourceType: "GeoFeature"), CoelbaAdmin);

        using var response = await EditAsync(Coelba, issued.Id, Rules(orderTypes: ["cut"]), CoelbaAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(response)).Should().Be(IssuedGrantProblems.Invalid);
    }

    // ── Revoke ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoking_DeactivatesTheGrant_AndItLeavesTheList()
    {
        var issued = await IssuedAsync(Coelba, Create(Bimbo), CoelbaAdmin);

        using (var revoked = await RevokeAsync(Coelba, issued.Id, CoelbaAdmin))
            revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using (var again = await RevokeAsync(Coelba, issued.Id, CoelbaAdmin))
            again.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _harness.GrantRowsAsync()).Single(g => g.Id == issued.Id).IsActive.Should().BeFalse();
        using var listed = await _harness.SendAsync(HttpMethod.Get, IssuedGrants(Coelba), CoelbaAdmin);
        (await ReadAsync<List<OrganizationGrantDto>>(listed)).Should().NotContain(g => g.Id == issued.Id);
    }
}
