using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using Xunit;
using static GeoAssets.Server.Tests.OrganizationScopedRoles.ScopedRolesHarness;

namespace GeoAssets.Server.Tests.OrganizationScopedRoles;

/// <summary>
/// XD01-327 — an organization's administrator assigns and revokes roles on it for users of the organizations it has
/// contracted: <c>{prefix}/organizations/{orgId}/scoped-roles</c>, <c>…/contracted-users</c>, <c>…/issued-grants</c> and
/// <c>{prefix}/users/{id}/scoped-roles</c>, over the production identity stack (<see cref="ScopedRolesHarness"/>).
/// </summary>
public sealed class OrganizationScopedRolesEndpointTests : IAsyncLifetime, IAsyncDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly ScopedRolesHarness _harness = new();

    public Task InitializeAsync() => _harness.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => _harness.DisposeAsync().AsTask();

    // The harness's dispose is idempotent, so a runner that also disposes the class as IAsyncDisposable is harmless.
    ValueTask IAsyncDisposable.DisposeAsync() => _harness.DisposeAsync();

    private static string ScopedRoles(Guid organizationId) => $"/api/identity/organizations/{organizationId}/scoped-roles";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(WebJson))!;

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    // ── The acceptance scenario ──────────────────────────────────────────────

    [Fact]
    public async Task CoelbasAdministrator_AssignsContractorC1ToABimboUser_SeesItListed_AndRevokingRemovesItFromTheUsersNextRequest()
    {
        var assign = await _harness.AssignAsync(Coelba, BimboTech, ContractorRoleId, asUser: CoelbaAdmin);

        assign.StatusCode.Should().Be(HttpStatusCode.Created);
        var assigned = await ReadAsync<ScopedRoleAssignmentDto>(assign);
        assigned.UserId.Should().Be(BimboTech);
        assigned.OrganizationId.Should().Be(Coelba);
        assigned.RoleName.Should().Be(ContractorRole);

        var list = await ReadAsync<List<ScopedRoleAssignmentDto>>(await _harness.SendAsync(HttpMethod.Get, ScopedRoles(Coelba), CoelbaAdmin));
        var listed = list.Should().ContainSingle().Subject;
        listed.Id.Should().Be(assigned.Id);
        listed.UserDisplayName.Should().Be("Tiago (BIMBO)");
        listed.UserEmail.Should().Be("tiago@bimbo.test");
        listed.UserOrganizationId.Should().Be(Bimbo);
        listed.UserOrganizationName.Should().Be("BIMBO");
        listed.OrganizationName.Should().Be("COELBA");
        listed.AssignedBy.Should().Be("coelba.admin@coelba.test");
        listed.AssignedAt.Should().Be(_harness.Time.GetUtcNow().UtcDateTime);

        var holding = await _harness.ContextOfNextRequestAsync(BimboTech);
        holding.HasRoleOn(Coelba, ContractorRole).Should().BeTrue();
        holding.HasPermissionOn(Coelba, "serviceorders:read").Should().BeTrue();
        holding.HasPermission("serviceorders:read").Should().BeFalse("a role on COELBA grants nothing at home");

        var revoke = await _harness.SendAsync(HttpMethod.Delete, $"{ScopedRoles(Coelba)}/{assigned.Id}", CoelbaAdmin);

        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAsync<List<ScopedRoleAssignmentDto>>(await _harness.SendAsync(HttpMethod.Get, ScopedRoles(Coelba), CoelbaAdmin)))
            .Should().BeEmpty();
        var revoked = await _harness.ContextOfNextRequestAsync(BimboTech);
        revoked.HasRoleOn(Coelba, ContractorRole).Should().BeFalse();
        revoked.HasPermissionOn(Coelba, "serviceorders:read").Should().BeFalse();
    }

    // ── Who administers the organization ─────────────────────────────────────

    [Fact]
    public async Task BimbosAdministrator_CannotAssignReadOrRevokeRolesScopedToCoelba()
    {
        await _harness.SeedScopedRoleAsync(BimboTech, ContractorRoleId, Coelba);
        var seeded = (await _harness.ScopedRowsAsync()).Single();

        var assign = await _harness.AssignAsync(Coelba, BimboTech2, ContractorRoleId, asUser: BimboAdmin);
        var read = await _harness.SendAsync(HttpMethod.Get, ScopedRoles(Coelba), BimboAdmin);
        var revoke = await _harness.SendAsync(HttpMethod.Delete, $"{ScopedRoles(Coelba)}/{seeded.Id}", BimboAdmin);
        var contracted = await _harness.SendAsync(HttpMethod.Get, $"/api/identity/organizations/{Coelba}/contracted-users", BimboAdmin);
        var grants = await _harness.SendAsync(HttpMethod.Get, $"/api/identity/organizations/{Coelba}/issued-grants", BimboAdmin);

        foreach (var response in new[] { assign, read, revoke, contracted, grants })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ProblemCodeAsync(response)).Should().Be(ScopedRoleAssignmentProblems.Forbidden);
        }

        (await _harness.ScopedRowsAsync()).Should().ContainSingle().Which.Id.Should().Be(seeded.Id, "nothing was assigned or revoked");
    }

    [Fact]
    public async Task ARoleHeldOnCoelba_NeverMakesItsHolderAnAdministratorOfCoelba()
    {
        // BIMBO's administrator holds Administrator on COELBA too (users:read + users:edit there): still a home-only decision.
        await _harness.SeedScopedRoleAsync(BimboAdmin, AdministratorRoleId, Coelba);

        var assign = await _harness.AssignAsync(Coelba, BimboTech, ContractorRoleId, asUser: BimboAdmin);

        assign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ACallerWithoutUsersEdit_MayReadButNotAssignOrRevoke()
    {
        await _harness.SeedScopedRoleAsync(BimboTech, ContractorRoleId, Coelba);
        var seeded = (await _harness.ScopedRowsAsync()).Single();

        (await _harness.SendAsync(HttpMethod.Get, ScopedRoles(Coelba), CoelbaReader)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _harness.AssignAsync(Coelba, BimboTech2, ContractorRoleId, asUser: CoelbaReader)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _harness.SendAsync(HttpMethod.Delete, $"{ScopedRoles(Coelba)}/{seeded.Id}", CoelbaReader)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _harness.ScopedRowsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task AMemberWithoutUsersRead_CannotReadTheAssignments()
    {
        var read = await _harness.SendAsync(HttpMethod.Get, ScopedRoles(Coelba), CoelbaMember);

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnAnonymousCaller_IsChallenged()
    {
        var read = await _harness.SendAsync(HttpMethod.Get, ScopedRoles(Coelba), asUser: null);
        var assign = await _harness.AssignAsync(Coelba, BimboTech, ContractorRoleId, asUser: null);

        read.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        assign.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task APlatformAdministrator_MayAdministerAnyOrganization_ButNotOneThatDoesNotExist()
    {
        var assign = await _harness.AssignAsync(Coelba, BimboTech, ContractorRoleId, asUser: PlatformAdmin);
        var unknown = await _harness.SendAsync(HttpMethod.Get, ScopedRoles(Guid.NewGuid()), PlatformAdmin);

        assign.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync<ScopedRoleAssignmentDto>(assign)).AssignedBy.Should().Be("root@platform.test");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(unknown)).Should().Be(ScopedRoleAssignmentProblems.NotFound);
    }

    // ── Who may be assigned ──────────────────────────────────────────────────

    [Theory]
    [InlineData("third-expired-grant")]
    [InlineData("inactive-user")]
    [InlineData("own-member")]
    [InlineData("no-organization")]
    public async Task AUserWhoseOrganizationHoldsNoActiveGrant_OrWhoIsNotEligible_IsRefusedWith409(string who)
    {
        var userId = who switch
        {
            "third-expired-grant" => ThirdTech,
            "inactive-user" => BimboInactive,
            "own-member" => CoelbaMember,
            _ => PlatformAdmin,
        };

        var assign = await _harness.AssignAsync(Coelba, userId, ContractorRoleId, asUser: CoelbaAdmin);

        assign.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(assign)).Should().Be(ScopedRoleAssignmentProblems.NotContracted);
        (await _harness.ScopedRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ARevokedGrant_NoLongerMakesItsOrganizationsUsersEligible()
    {
        await _harness.AddGrantAsync(grantee: Third, resource: Bimbo, isActive: false); // BIMBO revoked its grant to THIRD
        await _harness.AddGrantAsync(grantee: Bimbo, resource: Third);                  // THIRD's grant to BIMBO stands

        var refused = await _harness.AssignAsync(Bimbo, ThirdTech, ContractorRoleId, asUser: BimboAdmin);
        var allowed = await _harness.AssignAsync(Third, BimboTech, ContractorRoleId, asUser: PlatformAdmin);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task SelfAssignment_IsRefused()
    {
        var bySelf = await _harness.AssignAsync(Coelba, PlatformAdmin, ContractorRoleId, asUser: PlatformAdmin);
        var byAdmin = await _harness.AssignAsync(Coelba, CoelbaAdmin, ContractorRoleId, asUser: CoelbaAdmin);

        bySelf.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(bySelf)).Should().Be(ScopedRoleAssignmentProblems.SelfAssignment);
        byAdmin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(byAdmin)).Should().Be(ScopedRoleAssignmentProblems.SelfAssignment);
        (await _harness.ScopedRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownRoleOrAnEmptyBody_Is400_AndAnUnknownUser404()
    {
        var unknownRole = await _harness.AssignAsync(Coelba, BimboTech, Guid.NewGuid(), asUser: CoelbaAdmin);
        var emptyIds = await _harness.AssignAsync(Coelba, Guid.Empty, Guid.Empty, asUser: CoelbaAdmin);
        var unknownUser = await _harness.AssignAsync(Coelba, Guid.NewGuid(), ContractorRoleId, asUser: CoelbaAdmin);

        unknownRole.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(unknownRole)).Should().Be(ScopedRoleAssignmentProblems.Invalid);
        emptyIds.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknownUser.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(unknownUser)).Should().Be(ScopedRoleAssignmentProblems.NotFound);
    }

    [Fact]
    public async Task WhoAssignedItAndWhen_AreTheServers_NeverTheBodys()
    {
        var assign = await _harness.SendAsync(HttpMethod.Post, ScopedRoles(Coelba), CoelbaAdmin, new
        {
            userId = BimboTech,
            roleId = ContractorRoleId,
            assignedBy = "someone-else@forged.test",
            assignedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            organizationId = Third,
        });

        assign.StatusCode.Should().Be(HttpStatusCode.Created);
        var row = (await _harness.ScopedRowsAsync()).Single();
        row.AssignedBy.Should().Be("coelba.admin@coelba.test");
        row.AssignedAt.Should().Be(_harness.Time.GetUtcNow().UtcDateTime);
        row.OrganizationId.Should().Be(Coelba, "the organization is the route's");
    }

    [Fact]
    public async Task AssigningTheSameRoleAgain_AnswersTheExistingAssignment_WithoutASecondRow()
    {
        var first = await ReadAsync<ScopedRoleAssignmentDto>(await _harness.AssignAsync(Coelba, BimboTech, ContractorRoleId, asUser: CoelbaAdmin));

        var again = await _harness.AssignAsync(Coelba, BimboTech, ContractorRoleId, asUser: CoelbaAdmin);

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<ScopedRoleAssignmentDto>(again)).Id.Should().Be(first.Id);
        (await _harness.ScopedRowsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Revoking_AnUnknownAssignment_OrOneHeldOnAnotherOrganization_Is404_AndRevokesNothing()
    {
        await _harness.SeedScopedRoleAsync(BimboTech, ContractorRoleId, Third);
        var onThird = (await _harness.ScopedRowsAsync()).Single();

        var unknown = await _harness.SendAsync(HttpMethod.Delete, $"{ScopedRoles(Coelba)}/{Guid.NewGuid()}", CoelbaAdmin);
        var elsewhere = await _harness.SendAsync(HttpMethod.Delete, $"{ScopedRoles(Coelba)}/{onThird.Id}", CoelbaAdmin);

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        elsewhere.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _harness.ScopedRowsAsync()).Should().ContainSingle().Which.Id.Should().Be(onThird.Id);
    }

    // ── Eligibility reads ────────────────────────────────────────────────────

    [Fact]
    public async Task ContractedUsers_AreTheActiveUsersOfOrganizationsHoldingAnActiveUnexpiredGrant_AndNeverTheCaller()
    {
        var response = await _harness.SendAsync(HttpMethod.Get, $"/api/identity/organizations/{Coelba}/contracted-users", CoelbaAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = await ReadAsync<List<ContractedUserDto>>(response);
        users.Select(u => u.Id).Should().BeEquivalentTo([BimboAdmin, BimboTech, BimboTech2],
            "THIRD's grant expired, Inês is inactive, and COELBA's own members are not contractors");
        users.Should().OnlyContain(u => u.OrganizationId == Bimbo && u.OrganizationName == "BIMBO");

        var asPlatform = await ReadAsync<List<ContractedUserDto>>(
            await _harness.SendAsync(HttpMethod.Get, $"/api/identity/organizations/{Bimbo}/contracted-users", PlatformAdmin));
        asPlatform.Should().BeEmpty("BIMBO has contracted no one");
    }

    [Fact]
    public async Task IssuedGrants_AreTheActiveUnexpiredGrantsOfTheOrganization_WithTheirScopedRoleAndOrderTypes()
    {
        var response = await _harness.SendAsync(HttpMethod.Get, $"/api/identity/organizations/{Coelba}/issued-grants", CoelbaAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var grant = (await ReadAsync<List<OrganizationGrantDto>>(response)).Should().ContainSingle().Subject;
        grant.GranteeOrganizationId.Should().Be(Bimbo);
        grant.ResourceOrganizationId.Should().Be(Coelba);
        grant.RequiresScopedRole.Should().BeTrue();
        grant.OrderTypeIds.Should().Equal("cut");
    }

    // ── A user's roles on other organizations ────────────────────────────────

    [Fact]
    public async Task UsersScopedRoles_ShowEveryRoleToTheUsersOwnOrganizationAndThePlatform_ButOnlyItsOwnToAnotherOrganization()
    {
        await _harness.SeedScopedRoleAsync(BimboTech, ContractorRoleId, Coelba);
        await _harness.SeedScopedRoleAsync(BimboTech, ContractorRoleId, Third);
        var route = $"/api/identity/users/{BimboTech}/scoped-roles";

        var byCoelba = await ReadAsync<List<ScopedRoleAssignmentDto>>(await _harness.SendAsync(HttpMethod.Get, route, CoelbaAdmin));
        var byBimbo = await ReadAsync<List<ScopedRoleAssignmentDto>>(await _harness.SendAsync(HttpMethod.Get, route, BimboAdmin));
        var byPlatform = await ReadAsync<List<ScopedRoleAssignmentDto>>(await _harness.SendAsync(HttpMethod.Get, route, PlatformAdmin));

        byCoelba.Select(a => a.OrganizationName).Should().Equal(["COELBA"], "COELBA must not learn of BIMBO's contract with THIRD");
        byBimbo.Select(a => a.OrganizationName).Should().BeEquivalentTo(["COELBA", "THIRD"]);
        byPlatform.Should().HaveCount(2);
        byBimbo.Should().OnlyContain(a => a.UserId == BimboTech && a.RoleName == ContractorRole && a.UserOrganizationName == "BIMBO");
    }

    [Fact]
    public async Task UsersScopedRoles_OfAnUnknownUser_Is404()
    {
        var response = await _harness.SendAsync(HttpMethod.Get, $"/api/identity/users/{Guid.NewGuid()}/scoped-roles", CoelbaAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
