using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static GeoAssets.Server.Tests.OrganizationGrants.OrganizationGrantsHarness;

namespace GeoAssets.Server.Tests.OrganizationGrants;

/// <summary>
/// XD01-385 — the kernel's generic organization-grant API (<c>PUT/GET /api/identity/organization-grants</c>, <c>POST .../revoke</c>):
/// an idempotent upsert keyed by (grantee, resource, resourceType, scopeId), a revoke that deactivates, a paged list — all
/// service-principal-only, every write on the grant-sync outbox, the organizations never taken from a header.
/// </summary>
public sealed class OrganizationGrantsEndpointTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static async Task<OrganizationGrantUpsertResult> ReadUpsertAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<OrganizationGrantUpsertResult>(Web))!;

    private static async Task<OrganizationGrantPage> ReadPageAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<OrganizationGrantPage>(Web))!;

    private static async Task<string> ErrorOfAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetString()!;
    }

    private static async Task<OrganizationGrantsHarness> StartedAsync()
    {
        var harness = new OrganizationGrantsHarness();
        await harness.StartAsync();
        return harness;
    }

    // ── upsert ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upsert_NoActiveGrantWithTheKey_CreatesIt_StampedWithTheKernelsClockAndTheCaller()
    {
        await using var harness = await StartedAsync();

        using var response = await harness.UpsertAsync(UpsertBody(grantedBy: "prefeitura-admin@test"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadUpsertAsync(response);
        result.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Created);
        result.Grant.GranteeOrganizationId.Should().Be(Grantee);
        result.Grant.ResourceOrganizationId.Should().Be(Resource);
        result.Grant.ResourceType.Should().BeNull();
        result.Grant.ScopeId.Should().Be("3550308");
        result.Grant.AllowedActions.Should().Equal("ip-billing:read");
        result.Grant.RequiredRole.Should().BeNull();
        result.Grant.ExpiresAt.Should().BeNull();
        result.Grant.GrantedBy.Should().Be("prefeitura-admin@test");
        result.Grant.GrantedAt.Should().Be(harness.Time.GetUtcNow().UtcDateTime);
        result.Grant.IsActive.Should().BeTrue();
        (await harness.Db.OrganizationGrants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Upsert_Outcome_IsSerializedAsAString()
    {
        await using var harness = await StartedAsync();

        using var response = await harness.UpsertAsync(UpsertBody());

        (await response.Content.ReadAsStringAsync()).Should().Contain("\"outcome\":\"Created\"");
    }

    [Fact]
    public async Task Upsert_TheSameRequestAgain_IsUnchanged_AndAddsNothingToTheOutbox()
    {
        await using var harness = await StartedAsync();
        var created = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody()));
        var outboxBefore = (await harness.OutboxAsync()).Count;

        var again = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody()));

        again.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Unchanged);
        again.Grant.Id.Should().Be(created.Grant.Id);
        (await harness.Db.OrganizationGrants.CountAsync()).Should().Be(1);
        (await harness.OutboxAsync()).Should().HaveCount(outboxBefore, "an unchanged upsert writes nothing");
    }

    [Fact]
    public async Task Upsert_ActionsInAnotherOrder_AreStillUnchanged()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(UpsertBody(actions: ["ip-billing:read", "ip-billing:upload"]));

        var again = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(actions: ["ip-billing:upload", "ip-billing:read"])));

        again.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Unchanged);
    }

    [Fact]
    public async Task Upsert_ChangedActions_UpdatesTheSameGrant()
    {
        await using var harness = await StartedAsync();
        var created = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody()));

        var updated = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(actions: ["ip-billing:read", "ip-billing:validate"])));

        updated.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Updated);
        updated.Grant.Id.Should().Be(created.Grant.Id);
        updated.Grant.AllowedActions.Should().Equal("ip-billing:read", "ip-billing:validate");
        (await harness.Db.OrganizationGrants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Upsert_ChangedExpiry_UpdatesTheGrant_AndAClearedExpiryToo()
    {
        await using var harness = await StartedAsync();
        var expires = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await harness.UpsertAsync(UpsertBody());

        var withExpiry = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(expiresAt: expires)));
        var sameExpiry = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(expiresAt: expires)));
        var cleared = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody()));

        withExpiry.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Updated);
        withExpiry.Grant.ExpiresAt.Should().Be(expires);
        sameExpiry.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Unchanged);
        cleared.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Updated);
        cleared.Grant.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Upsert_ChangedRequiredRole_UpdatesTheGrant()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(UpsertBody());

        var updated = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(requiredRole: "Supervisor")));

        updated.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Updated);
        updated.Grant.RequiredRole.Should().Be("Supervisor");
    }

    [Fact]
    public async Task Upsert_GrantedByAndGrantedAt_AreSetOnCreationOnly()
    {
        await using var harness = await StartedAsync();
        var createdAt = harness.Time.GetUtcNow().UtcDateTime;
        await harness.UpsertAsync(UpsertBody(grantedBy: "first@test"));
        harness.Time.Advance(TimeSpan.FromDays(3));

        var updated = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(actions: ["ip-billing:read", "ip-billing:validate"], grantedBy: "second@test")));
        var unchanged = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(actions: ["ip-billing:read", "ip-billing:validate"], grantedBy: "third@test")));

        updated.Grant.GrantedBy.Should().Be("first@test");
        updated.Grant.GrantedAt.Should().Be(createdAt);
        unchanged.Grant.GrantedBy.Should().Be("first@test");
        unchanged.Grant.GrantedAt.Should().Be(createdAt);
    }

    [Fact]
    public async Task Upsert_DifferentKeys_AreDifferentGrants()
    {
        await using var harness = await StartedAsync();

        var unscoped = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(scopeId: null)));
        var scoped = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(scopeId: "3550308")));
        var otherScope = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(scopeId: "3304557")));
        var typed = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(resourceType: "GeoFeature")));
        var reversed = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(grantee: Resource, resource: Grantee)));
        var otherGrantee = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(grantee: Other)));

        new[] { unscoped, scoped, otherScope, typed, reversed, otherGrantee }
            .Should().OnlyContain(r => r.Outcome == OrganizationGrantUpsertOutcome.Created);
        (await harness.Db.OrganizationGrants.CountAsync()).Should().Be(6);
    }

    [Fact]
    public async Task Upsert_TheScopeAndResourceTypeAreMatchedOrdinally()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(UpsertBody(scopeId: "abc", resourceType: "GeoFeature"));

        var otherCase = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(scopeId: "ABC", resourceType: "GeoFeature")));
        var otherTypeCase = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody(scopeId: "abc", resourceType: "geofeature")));

        otherCase.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Created);
        otherTypeCase.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Created);
    }

    [Fact]
    public async Task Upsert_AfterTheGrantWasRevoked_CreatesANewGrant_NotReactivatingTheOld()
    {
        await using var harness = await StartedAsync();
        var first = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody()));
        await harness.RevokeAsync(KeyBody());

        var second = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody()));

        second.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Created);
        second.Grant.Id.Should().NotBe(first.Grant.Id);
        (await harness.Db.OrganizationGrants.Where(g => g.IsActive).CountAsync()).Should().Be(1);
        (await harness.Db.OrganizationGrants.CountAsync()).Should().Be(2, "a revoked grant is deactivated, never deleted");
    }

    [Fact]
    public async Task Upsert_WhenTheOnlyGrantWithTheKeyExpired_CreatesANewOne()
    {
        await using var harness = await StartedAsync();
        await harness.SeedAsync(expiresAt: harness.Time.GetUtcNow().UtcDateTime.AddDays(1));
        harness.Time.Advance(TimeSpan.FromDays(2));

        var result = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody()));

        result.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Created);
    }

    [Fact]
    public async Task Upsert_WithAnExpiryInThePast_IsRejected_AndCreatesNothing()
    {
        await using var harness = await StartedAsync();
        var past = harness.Time.GetUtcNow().UtcDateTime.AddDays(-1);

        using var response = await harness.UpsertAsync(UpsertBody(expiresAt: past));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("expiresAt must be in the future.");
        (await harness.Db.OrganizationGrants.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Upsert_WithAnExpiryExactlyNow_IsRejected()
    {
        await using var harness = await StartedAsync();

        using var response = await harness.UpsertAsync(UpsertBody(expiresAt: harness.Time.GetUtcNow().UtcDateTime));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upsert_EveryWrite_LandsOnTheGrantSyncOutbox_AsAFullSnapshot()
    {
        await using var harness = await StartedAsync();
        var expires = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var created = await ReadUpsertAsync(await harness.UpsertAsync(UpsertBody()));
        await harness.UpsertAsync(UpsertBody()); // unchanged: no entry
        await harness.UpsertAsync(UpsertBody(expiresAt: expires));
        await harness.RevokeAsync(KeyBody());

        var outbox = await harness.OutboxAsync();
        outbox.Should().HaveCount(3, "create, update and revoke each write one snapshot; the unchanged upsert none");
        outbox.Select(e => e.SequenceNumber).Should().BeInAscendingOrder();
        outbox.Should().OnlyContain(e => e.GrantId == created.Grant.Id && e.GranteeOrganizationId == Grantee && e.ResourceOrganizationId == Resource && e.ScopeId == "3550308");
        outbox.Select(e => e.IsActive).Should().Equal(true, true, false);
        outbox.Select(e => e.ExpiresAt).Should().Equal(null, expires, expires);
    }

    // ── validation ────────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, object, string> InvalidUpserts => new()
    {
        { "empty grantee", UpsertBody(grantee: Guid.Empty), "granteeOrganizationId is required." },
        { "empty resource", UpsertBody(resource: Guid.Empty), "resourceOrganizationId is required." },
        { "same organization on both sides", UpsertBody(grantee: Grantee, resource: Grantee), "A grant must cross organizations: grantee and resource organization are the same." },
        { "blank resource type", UpsertBody(resourceType: " "), "resourceType, when given, must not be blank." },
        { "empty resource type", UpsertBody(resourceType: ""), "resourceType, when given, must not be blank." },
        { "blank scope", UpsertBody(scopeId: "  "), "scopeId, when given, must not be blank." },
        { "no actions", UpsertBody(actions: []), "allowedActions must name at least one permission code." },
        { "blank action", UpsertBody(actions: ["ip-billing:read", " "]), "allowedActions must not contain a blank permission code." },
        { "blank required role", UpsertBody(requiredRole: " "), "requiredRole, when given, must not be blank." },
        { "no grantedBy", UpsertBody(grantedBy: ""), "grantedBy is required." },
        { "blank grantedBy", UpsertBody(grantedBy: "   "), "grantedBy is required." },
    };

    [Theory]
    [MemberData(nameof(InvalidUpserts))]
    public async Task Upsert_InvalidRequest_Returns400WithTheProblem_AndWritesNothing(string scenario, object body, string error)
    {
        await using var harness = await StartedAsync();

        using var response = await harness.UpsertAsync(body);

        (await ErrorOfAsync(response)).Should().Be(error, scenario);
        (await harness.Db.OrganizationGrants.AnyAsync()).Should().BeFalse();
        (await harness.OutboxAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_NullActions_Returns400()
    {
        await using var harness = await StartedAsync();

        using var response = await harness.UpsertAsync(new { key = KeyBody(), allowedActions = (string[]?)null, grantedBy = "x" });

        (await ErrorOfAsync(response)).Should().Be("allowedActions must name at least one permission code.");
    }

    [Fact]
    public async Task Upsert_MissingKey_Returns400()
    {
        await using var harness = await StartedAsync();

        using var response = await harness.UpsertAsync(new { allowedActions = new[] { "ip-billing:read" }, grantedBy = "x" });

        (await ErrorOfAsync(response)).Should().Be("key is required.");
    }

    [Fact]
    public async Task Upsert_NullBody_Returns400()
    {
        await using var harness = await StartedAsync();
        using var request = Request(HttpMethod.Put, "/api/identity/organization-grants", ServiceCaller);
        request.Content = new StringContent("null", Encoding.UTF8, "application/json");

        using var response = await harness.Client.SendAsync(request);

        (await ErrorOfAsync(response)).Should().Be("The grant is missing.");
    }

    [Fact]
    public async Task Upsert_MalformedJson_Returns400()
    {
        await using var harness = await StartedAsync();
        using var request = Request(HttpMethod.Put, "/api/identity/organization-grants", ServiceCaller);
        request.Content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        using var response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── revoke ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_NoActiveGrant_Answers200WithZero_NotAnError()
    {
        await using var harness = await StartedAsync();

        using var response = await harness.RevokeAsync(KeyBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<OrganizationGrantRevokeResult>(Web))!.Revoked.Should().Be(0);
        (await harness.OutboxAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_OneActiveGrant_DeactivatesIt_AndItLeavesTheList()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(UpsertBody());

        using var response = await harness.RevokeAsync(KeyBody());

        (await response.Content.ReadFromJsonAsync<OrganizationGrantRevokeResult>(Web))!.Revoked.Should().Be(1);
        (await ReadPageAsync(await harness.ListAsync(Grantee, Resource))).Items.Should().BeEmpty();
        (await harness.Db.OrganizationGrants.SingleAsync()).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_SeveralActiveGrantsWithTheKey_DeactivatesAllOfThem()
    {
        await using var harness = await StartedAsync();

        // Three active grants of one key can only exist here because the key has a null resourceType: the SQLite test
        // database treats nulls as distinct, so UX_OrganizationGrants_ActiveKey (XD01-394) does not see them as one key. On
        // PostgreSQL (NULLS NOT DISTINCT) they cannot be created — see OrganizationGrantsPostgresRepositoryTests; the revoke
        // still handles every active grant it finds, which is what this test pins.
        await harness.SeedAsync();
        await harness.SeedAsync();
        await harness.SeedAsync();

        using var response = await harness.RevokeAsync(KeyBody());

        (await response.Content.ReadFromJsonAsync<OrganizationGrantRevokeResult>(Web))!.Revoked.Should().Be(3);
        (await harness.OutboxAsync()).Should().HaveCount(3).And.OnlyContain(e => !e.IsActive);
    }

    [Fact]
    public async Task Revoke_AnAlreadyRevokedGrant_CountsZero()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(UpsertBody());
        await harness.RevokeAsync(KeyBody());

        using var again = await harness.RevokeAsync(KeyBody());

        (await again.Content.ReadFromJsonAsync<OrganizationGrantRevokeResult>(Web))!.Revoked.Should().Be(0);
    }

    [Fact]
    public async Task Revoke_OnlyTheGrantWithTheExactKey_IsDeactivated()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(UpsertBody(scopeId: "3550308"));
        await harness.UpsertAsync(UpsertBody(scopeId: "3304557"));
        await harness.UpsertAsync(UpsertBody(scopeId: null));
        await harness.UpsertAsync(UpsertBody(resourceType: "GeoFeature"));
        await harness.UpsertAsync(UpsertBody(grantee: Other));

        using var response = await harness.RevokeAsync(KeyBody(scopeId: "3550308"));

        (await response.Content.ReadFromJsonAsync<OrganizationGrantRevokeResult>(Web))!.Revoked.Should().Be(1);
        (await harness.Db.OrganizationGrants.Where(g => g.IsActive).CountAsync()).Should().Be(4);
        (await harness.Db.OrganizationGrants.SingleAsync(g => !g.IsActive)).ScopeId.Should().Be("3550308");
    }

    [Fact]
    public async Task Revoke_AnExpiredGrant_IsNotFoundByTheKey_SoItStaysFlaggedActive()
    {
        await using var harness = await StartedAsync();
        await harness.SeedAsync(expiresAt: harness.Time.GetUtcNow().UtcDateTime.AddDays(1));
        harness.Time.Advance(TimeSpan.FromDays(2));

        using var response = await harness.RevokeAsync(KeyBody());

        (await response.Content.ReadFromJsonAsync<OrganizationGrantRevokeResult>(Web))!.Revoked.Should().Be(0);
        (await harness.Db.OrganizationGrants.SingleAsync()).IsActive.Should().BeTrue("expired grants grant nothing, so they are left as they are");
    }

    public static TheoryData<string, object, string> InvalidKeys => new()
    {
        { "empty grantee", KeyBody(grantee: Guid.Empty), "granteeOrganizationId is required." },
        { "empty resource", KeyBody(resource: Guid.Empty), "resourceOrganizationId is required." },
        { "same organization", KeyBody(grantee: Grantee, resource: Grantee), "A grant must cross organizations: grantee and resource organization are the same." },
        { "blank resource type", KeyBody(resourceType: " "), "resourceType, when given, must not be blank." },
        { "blank scope", KeyBody(scopeId: " "), "scopeId, when given, must not be blank." },
    };

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public async Task Revoke_InvalidKey_Returns400WithTheProblem_AndRevokesNothing(string scenario, object body, string error)
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(UpsertBody());

        using var response = await harness.RevokeAsync(body);

        (await ErrorOfAsync(response)).Should().Be(error, scenario);
        (await harness.Db.OrganizationGrants.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_NullBody_Returns400()
    {
        await using var harness = await StartedAsync();
        using var request = Request(HttpMethod.Post, "/api/identity/organization-grants/revoke", ServiceCaller);
        request.Content = new StringContent("null", Encoding.UTF8, "application/json");

        using var response = await harness.Client.SendAsync(request);

        (await ErrorOfAsync(response)).Should().Be("The grant key is missing.");
    }

    // ── list ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsTheOrganizationPairsActiveGrants_OldestFirst_WithThePaging()
    {
        await using var harness = await StartedAsync();
        var start = harness.Time.GetUtcNow().UtcDateTime;
        await harness.SeedAsync(scopeId: "c", grantedAt: start.AddHours(2));
        await harness.SeedAsync(scopeId: "a", grantedAt: start);
        await harness.SeedAsync(scopeId: "b", grantedAt: start.AddHours(1));

        using var response = await harness.ListAsync(Grantee, Resource);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadPageAsync(response);
        page.Items.Select(i => i.ScopeId).Should().Equal("a", "b", "c");
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(100, "the default page size");
        page.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task List_Page2_ReturnsTheRest_AndAPageBeyondTheEndIsEmptyWithTheTotal()
    {
        await using var harness = await StartedAsync();
        var start = harness.Time.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 5; i++) await harness.SeedAsync(scopeId: $"s{i}", grantedAt: start.AddMinutes(i));

        var first = await ReadPageAsync(await harness.ListAsync(Grantee, Resource, "&page=1&pageSize=2"));
        var second = await ReadPageAsync(await harness.ListAsync(Grantee, Resource, "&page=2&pageSize=2"));
        var last = await ReadPageAsync(await harness.ListAsync(Grantee, Resource, "&page=3&pageSize=2"));
        var beyond = await ReadPageAsync(await harness.ListAsync(Grantee, Resource, "&page=4&pageSize=2"));

        first.Items.Select(i => i.ScopeId).Should().Equal("s0", "s1");
        second.Items.Select(i => i.ScopeId).Should().Equal("s2", "s3");
        last.Items.Select(i => i.ScopeId).Should().Equal("s4");
        beyond.Items.Should().BeEmpty();
        new[] { first, second, last, beyond }.Should().OnlyContain(p => p.TotalCount == 5 && p.PageSize == 2);
        beyond.Page.Should().Be(4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public async Task List_PageSizeAtTheBounds_IsAccepted(int pageSize)
    {
        await using var harness = await StartedAsync();

        using var response = await harness.ListAsync(Grantee, Resource, $"&pageSize={pageSize}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPageAsync(response)).PageSize.Should().Be(pageSize);
    }

    [Theory]
    [InlineData("&pageSize=0", "pageSize must be between 1 and 500.")]
    [InlineData("&pageSize=501", "pageSize must be between 1 and 500.")]
    [InlineData("&pageSize=-1", "pageSize must be between 1 and 500.")]
    [InlineData("&page=0", "page must be 1 or more.")]
    [InlineData("&page=-3", "page must be 1 or more.")]
    public async Task List_PageOrPageSizeOutOfRange_Returns400WithTheProblem(string query, string error)
    {
        await using var harness = await StartedAsync();

        using var response = await harness.ListAsync(Grantee, Resource, query);

        (await ErrorOfAsync(response)).Should().Be(error);
    }

    [Theory]
    [InlineData("&pageSize=abc")]
    [InlineData("&page=x")]
    public async Task List_NonNumericPaging_Returns400(string query)
    {
        await using var harness = await StartedAsync();

        using var response = await harness.ListAsync(Grantee, Resource, query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/identity/organization-grants")]
    [InlineData("/api/identity/organization-grants?granteeOrganizationId=a0a0a0a0-0000-4000-8000-000000000385")]
    [InlineData("/api/identity/organization-grants?resourceOrganizationId=b0b0b0b0-0000-4000-8000-000000000385")]
    [InlineData("/api/identity/organization-grants?granteeOrganizationId=nope&resourceOrganizationId=b0b0b0b0-0000-4000-8000-000000000385")]
    public async Task List_WithoutBothOrganizationsInTheQuery_Returns400(string url)
    {
        await using var harness = await StartedAsync();

        using var response = await harness.SendAsync(HttpMethod.Get, url, ServiceCaller);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_FiltersByGranteeAndResource_AndNeverShowsAnotherPair()
    {
        await using var harness = await StartedAsync();
        await harness.SeedAsync(grantee: Grantee, resource: Resource, scopeId: "mine");
        await harness.SeedAsync(grantee: Other, resource: Resource, scopeId: "other-grantee");
        await harness.SeedAsync(grantee: Grantee, resource: Other, scopeId: "other-resource");
        await harness.SeedAsync(grantee: Resource, resource: Grantee, scopeId: "reversed");

        var mine = await ReadPageAsync(await harness.ListAsync(Grantee, Resource));
        var reversed = await ReadPageAsync(await harness.ListAsync(Resource, Grantee));
        var none = await ReadPageAsync(await harness.ListAsync(Other, Other));

        mine.Items.Select(i => i.ScopeId).Should().Equal("mine");
        reversed.Items.Select(i => i.ScopeId).Should().Equal("reversed");
        none.Items.Should().BeEmpty();
        none.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_LeavesOutRevokedAndExpiredGrants()
    {
        await using var harness = await StartedAsync();
        var now = harness.Time.GetUtcNow().UtcDateTime;
        await harness.SeedAsync(scopeId: "live");
        await harness.SeedAsync(scopeId: "revoked", isActive: false);
        await harness.SeedAsync(scopeId: "expired", expiresAt: now.AddMinutes(-1));
        await harness.SeedAsync(scopeId: "expires-later", expiresAt: now.AddDays(1));

        var page = await ReadPageAsync(await harness.ListAsync(Grantee, Resource));

        page.Items.Select(i => i.ScopeId).Should().BeEquivalentTo("live", "expires-later");
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task List_ShowsGrantsThatWereJustUpserted_AndHidesThemOnceTheyExpire()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(UpsertBody(expiresAt: harness.Time.GetUtcNow().UtcDateTime.AddDays(1)));
        var before = await ReadPageAsync(await harness.ListAsync(Grantee, Resource));

        harness.Time.Advance(TimeSpan.FromDays(2));
        var after = await ReadPageAsync(await harness.ListAsync(Grantee, Resource));

        before.Items.Should().ContainSingle();
        after.Items.Should().BeEmpty();
    }

    // ── authorization ─────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> UserRoles => new() { "Administrator", "Supervisor,Administrator", "SomeOtherRole", "geoassets.servicecaller" };

    [Fact]
    public async Task EveryRoute_Anonymous_Returns401_AndWritesNothing()
    {
        await using var harness = await StartedAsync();

        using var upsert = await harness.UpsertAsync(UpsertBody(), roles: null);
        using var revoke = await harness.RevokeAsync(KeyBody(), roles: null);
        using var list = await harness.ListAsync(Grantee, Resource, roles: null);

        upsert.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        revoke.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await harness.Db.OrganizationGrants.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(UserRoles))]
    public async Task EveryRoute_AUserTokenWithoutTheServiceCallerAppRole_EvenAnAdministrator_Returns403_AndChangesNothing(string roles)
    {
        await using var harness = await StartedAsync();
        var seeded = await harness.SeedAsync();

        using var upsert = await harness.UpsertAsync(UpsertBody(scopeId: "new"), roles);
        using var revoke = await harness.RevokeAsync(KeyBody(), roles);
        using var list = await harness.ListAsync(Grantee, Resource, roles: roles);

        upsert.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await harness.Db.OrganizationGrants.CountAsync()).Should().Be(1);
        (await harness.Db.OrganizationGrants.SingleAsync(g => g.Id == seeded.Id)).IsActive.Should().BeTrue();
        (await harness.OutboxAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task EveryRoute_ServiceCallerCombinedWithOtherRoles_IsAllowed()
    {
        await using var harness = await StartedAsync();

        using var response = await harness.UpsertAsync(UpsertBody(), roles: "Administrator,GeoAssets.ServiceCaller");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheOrganizations_AreTheBodysAndTheQuerys_NeverTheCallersOrAHeader()
    {
        await using var harness = await StartedAsync();
        using var upsert = Request(HttpMethod.Put, "/api/identity/organization-grants", ServiceCaller, UpsertBody());
        upsert.Headers.Add(OrganizationHeader, Other.ToString());
        upsert.Headers.Add("X-Organization-Id", Other.ToString());

        using var created = await harness.Client.SendAsync(upsert);
        using var list = Request(HttpMethod.Get, $"/api/identity/organization-grants?granteeOrganizationId={Grantee}&resourceOrganizationId={Resource}", ServiceCaller);
        list.Headers.Add(OrganizationHeader, Other.ToString());
        list.Headers.Add("X-Organization-Id", Other.ToString());
        using var listed = await harness.Client.SendAsync(list);

        (await ReadUpsertAsync(created)).Grant.GranteeOrganizationId.Should().Be(Grantee);
        (await harness.Db.OrganizationGrants.SingleAsync()).ResourceOrganizationId.Should().Be(Resource);
        (await ReadPageAsync(listed)).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task TheCallersOwnOrganizationClaim_DoesNotLetItReadOrWriteAnotherPair()
    {
        await using var harness = await StartedAsync();
        await harness.SeedAsync(grantee: Grantee, resource: Resource);
        using var request = Request(HttpMethod.Get, $"/api/identity/organization-grants?granteeOrganizationId={Other}&resourceOrganizationId={Other}", ServiceCaller);
        request.Headers.Add(OrganizationHeader, Grantee.ToString());

        using var response = await harness.Client.SendAsync(request);

        (await ReadPageAsync(response)).Items.Should().BeEmpty("the query's organizations decide, not the caller's claim");
    }
}
