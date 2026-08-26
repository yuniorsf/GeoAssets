using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests;

public class EntraGraphUserInvitationProviderTests
{
    private const string TenantId = "94bb6627-6a6f-4219-b6d2-ce9ca5e82215";
    private const string TenantDomain = "geoassets.onmicrosoft.com";

    private sealed class StubGraphAccessTokenProvider(string token = "test-token") : IGraphAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult(token);
    }

    private static EntraGraphUserInvitationProvider BuildSut(FakeHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") },
            new StubGraphAccessTokenProvider(),
            new GraphCredentialOptions(TenantId, TenantDomain, "client-id", "client-secret"));

    private static JsonObject ReadBody(HttpRequestMessage request)
    {
        var text = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return (JsonObject)JsonNode.Parse(text)!;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, JsonNode node) =>
        new(status) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };

    // ── CreateInvitedAccountAsync ────────────────────────────────────────

    [Fact]
    public async Task CreateInvitedAccountAsync_PostsCorrectGraphUserPayload()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.Created, new JsonObject { ["id"] = "new-external-oid" }));
        var sut = BuildSut(handler);

        await sut.CreateInvitedAccountAsync("invitee@example.com", "Invitee Name");

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/users");

        var body = ReadBody(request);
        body["accountEnabled"]!.GetValue<bool>().Should().BeTrue();
        body["displayName"]!.GetValue<string>().Should().Be("Invitee Name");
        body["creationType"]!.GetValue<string>().Should().Be("LocalAccount");
        body["passwordProfile"]!["forceChangePasswordNextSignIn"]!.GetValue<bool>().Should().BeTrue();
        body["passwordProfile"]!["password"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();

        var identity = body["identities"]!.AsArray()[0]!;
        identity["signInType"]!.GetValue<string>().Should().Be("emailAddress");
        identity["issuer"]!.GetValue<string>().Should().Be(TenantDomain);
        identity["issuerAssignedId"]!.GetValue<string>().Should().Be("invitee@example.com");
    }

    [Fact]
    public async Task CreateInvitedAccountAsync_IdentityIssuer_IsTheTenantDomainNotTheGuid()
    {
        // XD01-91 regression test: Graph's Local Account identity creation rejects a GUID for
        // identities[].issuer with 400 Bad Request — it must be the tenant's domain name. Fails
        // without the fix (would assert TenantId, the GUID, instead).
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.Created, new JsonObject { ["id"] = "new-external-oid" }));
        var sut = BuildSut(handler);

        await sut.CreateInvitedAccountAsync("invitee@example.com", "Invitee Name");

        var issuer = ReadBody(handler.Requests[0])["identities"]!.AsArray()[0]!["issuer"]!.GetValue<string>();
        issuer.Should().Be(TenantDomain);
        issuer.Should().NotBe(TenantId);
    }

    [Fact]
    public async Task CreateInvitedAccountAsync_ReturnsExternalObjectIdFromGraphResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.Created, new JsonObject { ["id"] = "new-external-oid" }));
        var sut = BuildSut(handler);

        var externalObjectId = await sut.CreateInvitedAccountAsync("invitee@example.com", "Invitee Name");

        externalObjectId.Should().Be("new-external-oid");
    }

    [Fact]
    public async Task CreateInvitedAccountAsync_EachCallGeneratesADifferentPassword()
    {
        // Proves the "random" in the ticket's spec is real, not a fixed literal that would let
        // one invitee's throwaway password double as another's.
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.Created, new JsonObject { ["id"] = "external-oid" }));
        var sut = BuildSut(handler);

        await sut.CreateInvitedAccountAsync("first@example.com", "First");
        await sut.CreateInvitedAccountAsync("second@example.com", "Second");

        var password1 = ReadBody(handler.Requests[0])["passwordProfile"]!["password"]!.GetValue<string>();
        var password2 = ReadBody(handler.Requests[1])["passwordProfile"]!["password"]!.GetValue<string>();
        password1.Should().NotBe(password2);
    }

    // ── RevokeInvitedAccountAsync ────────────────────────────────────────

    [Fact]
    public async Task RevokeInvitedAccountAsync_PatchesAccountEnabledFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = BuildSut(handler);

        await sut.RevokeInvitedAccountAsync("external-oid-1");

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Patch);
        request.RequestUri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/users/external-oid-1");

        var body = ReadBody(request);
        body["accountEnabled"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task RevokeInvitedAccountAsync_TargetsOnlyTheSpecifiedAccountId()
    {
        // Non-leakage: revoking one invitee's account must never affect another's.
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = BuildSut(handler);

        await sut.RevokeInvitedAccountAsync("external-oid-a");
        await sut.RevokeInvitedAccountAsync("external-oid-b");

        handler.Requests[0].RequestUri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/users/external-oid-a");
        handler.Requests[1].RequestUri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/users/external-oid-b");
    }

    [Fact]
    public async Task RevokeInvitedAccountAsync_GraphReturns404_DoesNotThrow()
    {
        // XD01-94: the Graph account was already deleted out-of-band (e.g. manual cleanup) —
        // the goal ("this account can't sign in") is already satisfied, so this must not throw.
        // Fails without the fix (EnsureSuccessStatusCode would throw HttpRequestException).
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = BuildSut(handler);

        var act = () => sut.RevokeInvitedAccountAsync("already-deleted-oid");

        await act.Should().NotThrowAsync();
    }

    // ── Error handling ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateInvitedAccountAsync_GraphReturnsError_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var sut = BuildSut(handler);

        var act = () => sut.CreateInvitedAccountAsync("invitee@example.com", "Invitee Name");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RevokeInvitedAccountAsync_GraphReturnsForbidden_ThrowsHttpRequestException()
    {
        // Only 404 ("already gone") is tolerated — every other Graph error must still surface.
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var sut = BuildSut(handler);

        var act = () => sut.RevokeInvitedAccountAsync("external-oid-1");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
