using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// XD01-385 (AC: "Município grant sync works end-to-end via IpBilling.Server"): the real <c>GeoAssets.IpBilling.Server</c> host,
/// its <c>POST /api/ip-billing/municipio-grants/{sync,revoke}</c> endpoints and permission check (the kernel's answer for the
/// caller's token), its <c>RestOrganizationGrantClient</c> as <c>GeoAssets.ServiceCaller</c> — against the kernel's own grant
/// endpoints and repository (<see cref="StubKernel"/>).
/// </summary>
public sealed class MunicipioGrantBridgeEndToEndTests
{
    private const string Sync = "/api/ip-billing/municipio-grants/sync";
    private const string Revoke = "/api/ip-billing/municipio-grants/revoke";
    private const string IbgeCode = "3550308";

    private static readonly Guid Distribuidora = new("d0d0d0d0-0000-4000-8000-000000000385");
    private static readonly Guid PrefeituraA = new("a0a0a0a0-0000-4000-8000-000000000385");
    private static readonly Guid PrefeituraB = new("b0b0b0b0-0000-4000-8000-000000000385");

    private static object SyncBody(
        Guid? prefeitura, Guid? previous = null, string ibgeCode = IbgeCode, Guid? distribuidora = null, string grantedBy = "reference-manager@test") => new
        {
            ibgeCode,
            distribuidoraOrganizationId = distribuidora ?? Distribuidora,
            prefeituraOrganizationId = prefeitura,
            previousPrefeituraOrganizationId = previous,
            grantedBy,
        };

    private static object RevokeBody(Guid? prefeitura, string ibgeCode = IbgeCode) => new
    {
        ibgeCode,
        distribuidoraOrganizationId = Distribuidora,
        prefeituraOrganizationId = prefeitura,
    };

    private static async Task<HttpStatusCode> PostAsync(IpBillingBridgeHost host, string? token, string url, object? body)
    {
        using var client = host.ClientFor(token);
        using var response = await client.PostAsJsonAsync(new Uri(url, UriKind.Relative), body);
        return response.StatusCode;
    }

    // ── the grant sync works end to end ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_ByAReferenceDataManager_CreatesTheScopedGrantOnTheKernel()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));

        status.Should().Be(HttpStatusCode.NoContent);
        var grant = (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle().Subject;
        grant.GranteeOrganizationId.Should().Be(PrefeituraA);
        grant.ResourceOrganizationId.Should().Be(Distribuidora);
        grant.ResourceType.Should().BeNull("one grant covers every resource kind of the município");
        grant.ScopeId.Should().Be(IbgeCode);
        grant.AllowedActions.Should().Equal("ip-billing:read");
        grant.RequiredRole.Should().BeNull();
        grant.ExpiresAt.Should().BeNull();
        grant.GrantedBy.Should().Be("reference-manager@test");
        grant.GrantedAt.Should().Be(host.Kernel.Time.GetUtcNow().UtcDateTime, "the kernel's clock stamps it");
        grant.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Sync_AsTheServiceCaller_AlsoCreatesTheGrant_WithoutAskingTheKernelWhoTheCallerIs()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var status = await PostAsync(host, BridgeCallers.ServiceCallerToken, Sync, SyncBody(PrefeituraA));

        status.Should().Be(HttpStatusCode.NoContent);
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle();
        host.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().BeEmpty("a service principal satisfies the permission policy by its app role");
    }

    [Fact]
    public async Task Sync_TheGrantCallsGoToTheKernelAsTheServicePrincipal_AndTheIdentityCallWithTheCallersOwnToken()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));

        host.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().ContainSingle()
            .Which.Token.Should().Be(BridgeCallers.ReferenceManagerToken, "the caller's own token is forwarded to the identity route");
        host.Kernel.To(HttpMethod.Put, "/api/identity/organization-grants").Should().ContainSingle()
            .Which.Token.Should().Be(BridgeCallers.ServiceCallerToken, "the grant write is the service principal's, never the caller's");
    }

    [Fact]
    public async Task Sync_TheSameLinkAgain_LeavesTheGrantUnchanged()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));
        var outbox = (await host.Kernel.OutboxAsync()).Count;

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA, previous: PrefeituraA));

        status.Should().Be(HttpStatusCode.NoContent);
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle();
        (await host.Kernel.OutboxAsync()).Should().HaveCount(outbox, "a matching grant is left as it is, and nothing is revoked");
    }

    [Fact]
    public async Task Sync_RelinkToAnotherPrefeitura_RevokesTheOldGrant_AndGrantsTheNew()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraB, previous: PrefeituraA));

        status.Should().Be(HttpStatusCode.NoContent);
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
        (await host.Kernel.ActiveGrantsAsync(PrefeituraB, Distribuidora)).Should().ContainSingle().Which.ScopeId.Should().Be(IbgeCode);
        (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(host.Kernel.Db.OrganizationGrants)).Should().Be(2, "the old grant is deactivated, not deleted");
    }

    [Fact]
    public async Task Sync_ClearingThePrefeituraLink_RevokesThePreviousGrant_AndCreatesNone()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(prefeitura: null, previous: PrefeituraA));

        status.Should().Be(HttpStatusCode.NoContent);
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
        host.Kernel.To(HttpMethod.Put, "/api/identity/organization-grants").Should().ContainSingle("only the first sync upserted");
    }

    [Fact]
    public async Task Sync_ANewMunicipioWithoutAPrefeitura_CallsNothingOnTheKernel()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(prefeitura: null));

        status.Should().Be(HttpStatusCode.NoContent);
        host.Kernel.Requests.Should().NotContain(r => r.Path.Contains("organization-grants", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sync_TwoMunicipiosOfTheSamePair_AreTwoGrants_AndRevokingOneLeavesTheOther()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA, ibgeCode: "3550308"));
        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA, ibgeCode: "3304557"));

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Revoke, RevokeBody(PrefeituraA, ibgeCode: "3550308"));

        status.Should().Be(HttpStatusCode.NoContent);
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().ContainSingle().Which.ScopeId.Should().Be("3304557");
    }

    [Fact]
    public async Task Revoke_RemovesTheGrant_AndIsIdempotent()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));

        var first = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Revoke, RevokeBody(PrefeituraA));
        var again = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Revoke, RevokeBody(PrefeituraA));

        first.Should().Be(HttpStatusCode.NoContent);
        again.Should().Be(HttpStatusCode.NoContent);
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_WithoutAPrefeitura_CallsNothingOnTheKernel()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Revoke, RevokeBody(prefeitura: null));

        status.Should().Be(HttpStatusCode.NoContent);
        host.Kernel.Requests.Should().NotContain(r => r.Path.Contains("organization-grants", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryWriteOfTheBridge_LandsOnTheKernelsGrantSyncOutbox()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));
        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraB, previous: PrefeituraA));
        await PostAsync(host, BridgeCallers.ReferenceManagerToken, Revoke, RevokeBody(PrefeituraB));

        var outbox = await host.Kernel.OutboxAsync();
        outbox.Select(e => (e.GranteeOrganizationId, e.IsActive)).Should().Equal(
            (PrefeituraA, true), (PrefeituraA, false), (PrefeituraB, true), (PrefeituraB, false));
        outbox.Should().OnlyContain(e => e.ScopeId == IbgeCode && e.ResourceOrganizationId == Distribuidora);
    }

    // ── who may call it ───────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BridgeCallers.ReaderToken)]
    [InlineData(BridgeCallers.AdministratorToken)]
    public async Task BothEndpoints_ACallerWithoutTheReferenceManagePermission_Get403_AndTheKernelSeesNoGrantCall(string token)
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var sync = await PostAsync(host, token, Sync, SyncBody(PrefeituraA));
        var revoke = await PostAsync(host, token, Revoke, RevokeBody(PrefeituraA));

        sync.Should().Be(HttpStatusCode.Forbidden);
        revoke.Should().Be(HttpStatusCode.Forbidden);
        host.Kernel.Requests.Should().NotContain(r => r.Path.Contains("organization-grants", StringComparison.Ordinal));
        host.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().HaveCount(2).And.OnlyContain(r => r.Token == token);
    }

    [Fact]
    public async Task BothEndpoints_Anonymous_Get401_WithoutTouchingTheKernel()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        var before = host.Kernel.Requests.Count(r => !r.Path.Contains("/verticals/", StringComparison.Ordinal));

        var sync = await PostAsync(host, token: null, Sync, SyncBody(PrefeituraA));
        var revoke = await PostAsync(host, token: null, Revoke, RevokeBody(PrefeituraA));

        sync.Should().Be(HttpStatusCode.Unauthorized);
        revoke.Should().Be(HttpStatusCode.Unauthorized);
        host.Kernel.Requests.Count(r => !r.Path.Contains("/verticals/", StringComparison.Ordinal)).Should().Be(before);
    }

    [Fact]
    public async Task BothEndpoints_AnUnknownBearerToken_Gets401()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        (await PostAsync(host, "not-a-known-token", Sync, SyncBody(PrefeituraA))).Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(host, "not-a-known-token", Revoke, RevokeBody(PrefeituraA))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheOrganizations_AreTheBodys_NotTheCallers_AReferenceManagerProvisionsAnyPair()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        using var client = host.ClientFor(BridgeCallers.ReferenceManagerToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Sync, UriKind.Relative)) { Content = JsonContent.Create(SyncBody(PrefeituraB)) };
        request.Headers.Add("X-Organization-Id", PrefeituraA.ToString());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.Kernel.ActiveGrantsAsync(PrefeituraB, Distribuidora)).Should().ContainSingle();
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty("no header selects the organization");
    }

    // ── when the kernel fails ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_WhenTheKernelIsDown_ForTheServiceCaller_Answers503()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        host.Link.Down = true;

        var status = await PostAsync(host, BridgeCallers.ServiceCallerToken, Sync, SyncBody(PrefeituraA));

        status.Should().Be(HttpStatusCode.ServiceUnavailable, "an unreachable kernel is this host's dependency being unavailable");
        host.Unhandled.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_WhenTheKernelIsDown_ForAUser_Answers503_AlreadyAtThePermissionCheck()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        host.Link.Down = true;

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));

        status.Should().Be(HttpStatusCode.ServiceUnavailable, "the kernel's /api/identity/me cannot be reached");
        host.Unhandled.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_WhenTheKernelRejectsTheCallersToken_TheAudienceMismatch_Answers502TokenRejected_NotAnUnhandled500()
    {
        // The ADR's audience risk: this host validates the token (its own audience), the kernel's /api/identity/me does not
        // accept it and answers 401. That is a deployment misconfiguration the caller cannot fix by signing in again, so the
        // host answers 502 kernel.token-rejected (XD01-390; 503 kernel.unavailable before) rather than an unhandled 500.
        await using var host = await IpBillingBridgeHost.StartAsync();

        using var client = host.ClientFor(BridgeCallers.WrongAudienceToken);
        using var response = await client.PostAsJsonAsync(new Uri(Sync, UriKind.Relative), SyncBody(PrefeituraA));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"kernel.token-rejected\"");
        host.Unhandled.Should().BeEmpty();
        host.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().ContainSingle();
        host.Kernel.Requests.Should().NotContain(r => r.Path.Contains("organization-grants", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sync_WhenTheKernelRefusesTheUpsertAsAConflict_Answers409KernelConflict_NotA503()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        host.Kernel.GrantUpsertConflicts = true;

        using var client = host.ClientFor(BridgeCallers.ReferenceManagerToken);
        using var response = await client.PostAsJsonAsync(new Uri(Sync, UriKind.Relative), SyncBody(PrefeituraA));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"kernel.conflict\"");
        host.Unhandled.Should().BeEmpty();
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_WhenTheServicePrincipalIsNotAServiceCallerAtTheKernel_Answers503_AndNothingIsGranted()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        host.ServiceTokens.Token = BridgeCallers.ReferenceManagerToken;

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));

        status.Should().Be(HttpStatusCode.ServiceUnavailable, "the kernel refusing this host's own credentials (403) is this host's misconfiguration");
        host.Unhandled.Should().BeEmpty();
        (await host.Kernel.ActiveGrantsAsync(PrefeituraA, Distribuidora)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_WithAServiceTokenTheKernelDoesNotKnow_TheKernelAnswers401_AndTheHostAnswers503()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        host.ServiceTokens.Token = "not-a-known-token";

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(PrefeituraA));

        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        host.Unhandled.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_PrefeituraEqualToDistribuidora_IsRefusedByTheKernelWith400_WhichTheHostAnswersAs400()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var status = await PostAsync(host, BridgeCallers.ReferenceManagerToken, Sync, SyncBody(Distribuidora));

        status.Should().Be(HttpStatusCode.BadRequest);
        host.Unhandled.Should().BeEmpty();
        (await host.Kernel.ActiveGrantsAsync(Distribuidora, Distribuidora)).Should().BeEmpty();
    }

    // ── bad requests ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_WithoutABody_Answers400()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        using var client = host.ClientFor(BridgeCallers.ServiceCallerToken);

        using var response = await client.PostAsync(new Uri(Sync, UriKind.Relative), new StringContent(string.Empty, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sync_WithMalformedJson_Answers400()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        using var client = host.ClientFor(BridgeCallers.ServiceCallerToken);

        using var response = await client.PostAsync(new Uri(Sync, UriKind.Relative), new StringContent("{ nope", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Sync_WithABlankIbgeCode_Answers400_NotAnUnhandledException(string ibgeCode)
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var status = await PostAsync(host, BridgeCallers.ServiceCallerToken, Sync, SyncBody(PrefeituraA, ibgeCode: ibgeCode));

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sync_WithABlankGrantedBy_Answers400_NotAnUnhandledException()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var status = await PostAsync(host, BridgeCallers.ServiceCallerToken, Sync, SyncBody(PrefeituraA, grantedBy: " "));

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sync_WithABlankIbgeCode_Answers400_AndCallsNothingOnTheKernel()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var status = await PostAsync(host, BridgeCallers.ServiceCallerToken, Sync, SyncBody(PrefeituraA, ibgeCode: " "));

        status.Should().Be(HttpStatusCode.BadRequest);
        host.Unhandled.Should().BeEmpty();
        host.Kernel.Requests.Should().NotContain(r => r.Path.Contains("organization-grants", StringComparison.Ordinal));
    }

    // ── the retired route ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheRetiredKernelPath_IsNotServedByThisHost()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        var status = await PostAsync(host, BridgeCallers.ServiceCallerToken, "/api/identity/municipio-grants/sync", SyncBody(PrefeituraA));

        status.Should().Be(HttpStatusCode.NotFound);
    }
}
