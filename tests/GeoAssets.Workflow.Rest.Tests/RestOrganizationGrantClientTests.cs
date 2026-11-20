using System.Net;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// <see cref="RestOrganizationGrantClient"/> (XD01-385): the kernel's generic organization-grant API as a vertical host's
/// service principal calls it — URLs relative to the kernel's root, the bodies the kernel's endpoints read, a fresh bearer per
/// call, and every kernel refusal or transport failure surfaced as <see cref="HttpRequestException"/>.
/// </summary>
public sealed class RestOrganizationGrantClientTests
{
    private static readonly Guid Grantee = new("a0a0a0a0-0000-4000-8000-000000000385");
    private static readonly Guid Resource = new("b0b0b0b0-0000-4000-8000-000000000385");

    private static readonly OrganizationGrantKey Key = new(Grantee, Resource, ResourceType: null, ScopeId: "3550308");

    private static readonly OrganizationGrantUpsertRequest Upsert = new(
        Key, ["ip-billing:read"], RequiredRole: null, ExpiresAt: null, GrantedBy: "admin@test");

    private static string GrantJson(string outcome = "Created") =>
        $$"""
        { "grant": { "id": "c0c0c0c0-0000-4000-8000-000000000385", "granteeOrganizationId": "{{Grantee}}", "resourceOrganizationId": "{{Resource}}",
            "resourceType": null, "scopeId": "3550308", "allowedActions": ["ip-billing:read"], "requiredRole": null, "expiresAt": null,
            "grantedBy": "admin@test", "grantedAt": "2026-11-20T12:00:00Z", "isActive": true },
          "outcome": "{{outcome}}" }
        """;

    private static RestOrganizationGrantClient Sut(RecordingKernelHandler handler, FakeServiceAuthTokenProvider? tokens = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://kernel.test/") }, tokens);

    private static RecordingKernelHandler Answers(HttpStatusCode status, string json) => new(_ => RecordingKernelHandler.Json(status, json));

    [Fact]
    public async Task UpsertAsync_PutsTheRequest_ToTheGrantsRoute_AndReturnsTheKernelsAnswer()
    {
        var handler = Answers(HttpStatusCode.OK, GrantJson("Updated"));

        var result = await Sut(handler).UpsertAsync(Upsert, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.Original.Should().Be("http://kernel.test/api/identity/organization-grants");
        var key = request.Json.GetProperty("key");
        key.GetProperty("granteeOrganizationId").GetGuid().Should().Be(Grantee);
        key.GetProperty("resourceOrganizationId").GetGuid().Should().Be(Resource);
        key.GetProperty("resourceType").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        key.GetProperty("scopeId").GetString().Should().Be("3550308");
        request.Json.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).Should().Equal("ip-billing:read");
        request.Json.GetProperty("grantedBy").GetString().Should().Be("admin@test");
        result.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Updated);
        result.Grant.ScopeId.Should().Be("3550308");
        result.Grant.AllowedActions.Should().Equal("ip-billing:read");
        result.Grant.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("Created", OrganizationGrantUpsertOutcome.Created)]
    [InlineData("Updated", OrganizationGrantUpsertOutcome.Updated)]
    [InlineData("Unchanged", OrganizationGrantUpsertOutcome.Unchanged)]
    public async Task UpsertAsync_ReadsEveryOutcome(string wire, OrganizationGrantUpsertOutcome expected)
    {
        var result = await Sut(Answers(HttpStatusCode.OK, GrantJson(wire))).UpsertAsync(Upsert, CancellationToken.None);

        result.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task UpsertAsync_SendsTheExpiryAndRequiredRole_WhenTheyAreGiven()
    {
        var handler = Answers(HttpStatusCode.OK, GrantJson());
        var expires = new DateTime(2027, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        await Sut(handler).UpsertAsync(Upsert with { RequiredRole = "Supervisor", ExpiresAt = expires }, CancellationToken.None);

        handler.Requests.Single().Json.GetProperty("requiredRole").GetString().Should().Be("Supervisor");
        handler.Requests.Single().Json.GetProperty("expiresAt").GetDateTime().Should().Be(expires);
    }

    [Fact]
    public async Task RevokeAsync_PostsTheKey_ToTheRevokeRoute_AndReturnsTheCount()
    {
        var handler = Answers(HttpStatusCode.OK, """{ "revoked": 2 }""");

        var result = await Sut(handler).RevokeAsync(Key, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Original.Should().Be("http://kernel.test/api/identity/organization-grants/revoke");
        request.Json.GetProperty("granteeOrganizationId").GetGuid().Should().Be(Grantee);
        request.Json.GetProperty("resourceOrganizationId").GetGuid().Should().Be(Resource);
        request.Json.GetProperty("scopeId").GetString().Should().Be("3550308");
        result.Revoked.Should().Be(2);
    }

    [Fact]
    public async Task ListActiveAsync_GetsTheOrganizationPairsPage_WithTheOrganizationsInTheQuery()
    {
        var handler = Answers(HttpStatusCode.OK, $$"""{ "items": [], "page": 2, "pageSize": 50, "totalCount": 51 }""");

        var page = await Sut(handler).ListActiveAsync(Grantee, Resource, page: 2, pageSize: 50, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Original.Should().Be(
            $"http://kernel.test/api/identity/organization-grants?granteeOrganizationId={Grantee}&resourceOrganizationId={Resource}&page=2&pageSize=50");
        request.Body.Should().BeNull();
        page.Should().BeEquivalentTo(new OrganizationGrantPage([], 2, 50, 51));
    }

    [Fact]
    public async Task ListActiveAsync_ReadsTheItemsOfThePage()
    {
        var item = GrantJson().Replace("\"outcome\": \"Created\"", "\"x\": 0", StringComparison.Ordinal);
        var json = $$"""{ "items": [ {{System.Text.Json.JsonDocument.Parse(item).RootElement.GetProperty("grant").GetRawText()}} ], "page": 1, "pageSize": 100, "totalCount": 1 }""";

        var page = await Sut(Answers(HttpStatusCode.OK, json)).ListActiveAsync(Grantee, Resource, 1, 100, CancellationToken.None);

        page.Items.Should().ContainSingle().Which.GrantedBy.Should().Be("admin@test");
        page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListActiveAsync_TheOrganizationsAreTheCallersArguments_NeverAHeader()
    {
        var handler = Answers(HttpStatusCode.OK, """{ "items": [], "page": 1, "pageSize": 100, "totalCount": 0 }""");

        await Sut(handler, new FakeServiceAuthTokenProvider()).ListActiveAsync(Grantee, Resource, 1, 100, CancellationToken.None);

        handler.Requests.Single().Headers.Keys.Should().NotContain(k => k.Contains("organization", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    public async Task ListActiveAsync_WithAPageOrSizeBelowOne_ThrowsBeforeAnyRequest(int page, int pageSize)
    {
        var handler = Answers(HttpStatusCode.OK, "{}");

        var act = async () => await Sut(handler).ListActiveAsync(Grantee, Resource, page, pageSize, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryCall_CarriesAFreshServiceBearerToken()
    {
        var handler = new RecordingKernelHandler(request => request.Path.EndsWith("/revoke", StringComparison.Ordinal)
            ? RecordingKernelHandler.Json(HttpStatusCode.OK, """{ "revoked": 0 }""")
            : request.Method == HttpMethod.Get
                ? RecordingKernelHandler.Json(HttpStatusCode.OK, """{ "items": [], "page": 1, "pageSize": 100, "totalCount": 0 }""")
                : RecordingKernelHandler.Json(HttpStatusCode.OK, GrantJson()));
        var sut = Sut(handler, new FakeServiceAuthTokenProvider());

        await sut.UpsertAsync(Upsert, CancellationToken.None);
        await sut.RevokeAsync(Key, CancellationToken.None);
        await sut.ListActiveAsync(Grantee, Resource, 1, 100, CancellationToken.None);

        handler.Requests.Select(r => r.Authorization).Should().Equal("Bearer service-token-1", "Bearer service-token-2", "Bearer service-token-3");
    }

    [Fact]
    public async Task WithoutATokenProvider_NoAuthorizationIsSent()
    {
        var handler = Answers(HttpStatusCode.OK, """{ "revoked": 0 }""");

        await Sut(handler).RevokeAsync(Key, CancellationToken.None);

        handler.Requests.Single().Authorization.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task EveryCall_WhenTheKernelRefuses_ThrowsHttpRequestException_WithTheStatus(HttpStatusCode status)
    {
        var sut = Sut(new RecordingKernelHandler(_ => new HttpResponseMessage(status)));

        var upsert = async () => await sut.UpsertAsync(Upsert, CancellationToken.None);
        var revoke = async () => await sut.RevokeAsync(Key, CancellationToken.None);
        var list = async () => await sut.ListActiveAsync(Grantee, Resource, 1, 100, CancellationToken.None);

        (await upsert.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
        (await revoke.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
        (await list.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task UpsertAsync_WhenTheKernelAnswers409OrganizationGrantConflict_ThrowsTheTypedException_WithTheKeyAndTheKernelsDetail()
    {
        var sut = Sut(new RecordingKernelHandler(_ => RecordingKernelHandler.Problem(HttpStatusCode.Conflict, "organization-grant.conflict")));

        var act = async () => await sut.UpsertAsync(Upsert, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<OrganizationGrantConflictException>()).Which;
        thrown.StatusCode.Should().Be(HttpStatusCode.Conflict);
        thrown.Key.Should().Be(Key);
        thrown.KernelDetail.Should().Be("Detail of organization-grant.conflict");
        thrown.Should().BeAssignableTo<HttpRequestException>();
        OrganizationGrantConflictException.ProblemCode.Should().Be("organization-grant.conflict");
    }

    [Fact]
    public async Task UpsertAsync_WhenTheConflictProblemHasNoDetail_ThrowsTheTypedException_WithoutKernelDetail()
    {
        var sut = Sut(Answers(HttpStatusCode.Conflict, """{ "code": "organization-grant.conflict" }"""));

        var act = async () => await sut.UpsertAsync(Upsert, CancellationToken.None);

        (await act.Should().ThrowAsync<OrganizationGrantConflictException>()).Which.KernelDetail.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{ "title": "no code" }""")]
    [InlineData("""{ "code": 7 }""")]
    [InlineData("""{ "code": "target-ledger.conflict" }""")]
    [InlineData("""{ "code": "Organization-Grant.Conflict" }""")]
    public async Task UpsertAsync_WhenTheKernelAnswers409WithoutTheGrantConflictCode_ThrowsAPlainHttpRequestException(string body)
    {
        var sut = Sut(new RecordingKernelHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(body) }));

        var act = async () => await sut.UpsertAsync(Upsert, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        thrown.Should().NotBeOfType<OrganizationGrantConflictException>();
        thrown.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpsertAsync_WhenTheKernelAnswers409WithoutABody_ThrowsAPlainHttpRequestException()
    {
        var sut = Sut(new RecordingKernelHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)));

        var act = async () => await sut.UpsertAsync(Upsert, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        thrown.Should().NotBeOfType<OrganizationGrantConflictException>();
        thrown.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task UpsertAsync_TheGrantConflictCodeOnAnotherStatus_IsAPlainHttpRequestException(HttpStatusCode status)
    {
        var sut = Sut(new RecordingKernelHandler(_ => RecordingKernelHandler.Problem(status, "organization-grant.conflict")));

        var act = async () => await sut.UpsertAsync(Upsert, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        thrown.Should().NotBeOfType<OrganizationGrantConflictException>();
        thrown.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task RevokeAndList_WhenTheKernelAnswers409OrganizationGrantConflict_ThrowAPlainHttpRequestException()
    {
        var sut = Sut(new RecordingKernelHandler(_ => RecordingKernelHandler.Problem(HttpStatusCode.Conflict, "organization-grant.conflict")));

        var revoke = async () => await sut.RevokeAsync(Key, CancellationToken.None);
        var list = async () => await sut.ListActiveAsync(Grantee, Resource, 1, 100, CancellationToken.None);

        var revoked = (await revoke.Should().ThrowAsync<HttpRequestException>()).Which;
        revoked.Should().NotBeOfType<OrganizationGrantConflictException>();
        revoked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var listed = (await list.Should().ThrowAsync<HttpRequestException>()).Which;
        listed.Should().NotBeOfType<OrganizationGrantConflictException>();
        listed.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task EveryCall_WhenTheConnectionFails_ThrowsHttpRequestException_WithoutAStatus()
    {
        var sut = Sut(new RecordingKernelHandler(_ => throw new HttpRequestException("connection refused")));

        var upsert = async () => await sut.UpsertAsync(Upsert, CancellationToken.None);
        var revoke = async () => await sut.RevokeAsync(Key, CancellationToken.None);
        var list = async () => await sut.ListActiveAsync(Grantee, Resource, 1, 100, CancellationToken.None);

        (await upsert.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().BeNull();
        await revoke.Should().ThrowAsync<HttpRequestException>();
        await list.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task EveryCall_WhenTheKernelAnswersAnEmptyJsonBody_ThrowsHttpRequestException_NamingTheRoute()
    {
        var sut = Sut(Answers(HttpStatusCode.OK, "null"));

        var upsert = async () => await sut.UpsertAsync(Upsert, CancellationToken.None);
        var revoke = async () => await sut.RevokeAsync(Key, CancellationToken.None);
        var list = async () => await sut.ListActiveAsync(Grantee, Resource, 1, 100, CancellationToken.None);

        (await upsert.Should().ThrowAsync<HttpRequestException>()).WithMessage("*PUT /api/identity/organization-grants*empty body*");
        (await revoke.Should().ThrowAsync<HttpRequestException>()).WithMessage("*POST /api/identity/organization-grants*empty body*");
        (await list.Should().ThrowAsync<HttpRequestException>()).WithMessage("*GET /api/identity/organization-grants*empty body*");
    }

    [Fact]
    public Task UpsertAsync_WhenTheKernelAnswers200WithAnEmptyBody_ThrowsAJsonException_NotAnHttpRequestException()
    {
        // Documents today's behaviour: a 200 with no content at all (a proxy that swallows the body) is a JsonException, not
        // an HttpRequestException — a caller catching only HttpRequestException would not see it.
        var sut = Sut(new RecordingKernelHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) }));

        var act = async () => await sut.UpsertAsync(Upsert, CancellationToken.None);

        return act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task EveryCall_RefusesMissingArguments_BeforeAnyRequest()
    {
        var handler = Answers(HttpStatusCode.OK, "{}");
        var sut = Sut(handler);

        var upsert = async () => await sut.UpsertAsync(null!, CancellationToken.None);
        var revoke = async () => await sut.RevokeAsync(null!, CancellationToken.None);

        await upsert.Should().ThrowAsync<ArgumentNullException>();
        await revoke.Should().ThrowAsync<ArgumentNullException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryCall_WhenTheCallerCancels_ThrowsTheCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var sut = Sut(Answers(HttpStatusCode.OK, GrantJson()));

        var act = async () => await sut.UpsertAsync(Upsert, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
