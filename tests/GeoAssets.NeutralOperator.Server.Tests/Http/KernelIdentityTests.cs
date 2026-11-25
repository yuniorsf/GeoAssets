using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Hosting;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// This host has no identity database (XD01-382, ADR-001 D3): who the caller is, what it may do, which grants its organization holds and
/// which roles it holds on other organizations (XD01-409) are the kernel's answers (<see cref="KernelGeoAuthorizationService"/>,
/// <see cref="KernelOwnOrganizationGrantSource"/>) — each route read at most once per request, and never more generous than the kernel.
/// </summary>
public sealed class KernelIdentityTests
{
    private static readonly Guid User = new("00000000-0000-4000-8000-000000000a01");
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly Guid Organization = new("a0a0a0a0-0000-4000-8000-000000000382");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, Wire), Encoding.UTF8, "application/json") };

    private static HttpClient Client(StubHandler handler) => new(handler) { BaseAddress = new Uri("http://kernel.test/api/identity/") };

    private static AuthorizationContextDto Me() => new(
        User, "user@example.test", "User", Organization, ["Administrator"], [new ClaimDto("tier", "gold")], ["serviceorders:read"]);

    private const string MePath = "/api/identity/me";
    private const string EntitlementsPath = "/api/identity/me/cross-org-entitlements";

    private static CrossOrgEntitlementsDto Entitlements() => new(
        [new CrossOrgGrantEntitlementDto(TestIdentities.OtherOrganization, "Other", "ServiceOrder", "district-7", ["cut"],
            ["serviceorders:view", "serviceorders:execute"], "Supervisor", RequiresScopedRole: true, ExpiresAt: null)],
        [new ScopedRoleEntitlementDto(TestIdentities.OtherOrganization, "Other", ["Contractor C1"], ["serviceorders:view", "serviceorders:execute"])]);

    /// <summary>A kernel answering <c>/me</c> and <c>/me/cross-org-entitlements</c>; <paramref name="entitlements"/> answers the latter.</summary>
    private static StubHandler Kernel(Func<HttpRequestMessage, HttpResponseMessage>? entitlements = null) => new(request =>
        string.Equals(request.RequestUri!.AbsolutePath, EntitlementsPath, StringComparison.Ordinal)
            ? (entitlements ?? (_ => Json(Entitlements())))(request)
            : Json(Me()));

    // ── KernelGeoAuthorizationService ─────────────────────────────────────────

    [Fact]
    public async Task TheCallersContext_IsReadFromTheKernelsMeRoute_OnceForAllTheQuestionsOfARequest()
    {
        var handler = Kernel();
        var service = new KernelGeoAuthorizationService(Client(handler));

        (await service.IsInRoleAsync("Administrator")).Should().BeTrue();
        (await service.IsInRoleAsync("Crew")).Should().BeFalse();
        (await service.HasPermissionAsync("serviceorders:read")).Should().BeTrue();
        (await service.HasPermissionAsync("serviceorders:delete")).Should().BeFalse();
        (await service.HasClaimAsync("tier", "gold")).Should().BeTrue();
        (await service.HasClaimAsync("tier", "silver")).Should().BeFalse();
        handler.Paths.Should().Equal([MePath], "home checks never need the cross-organization entitlements");
        var context = await service.GetAuthorizationContextAsync();
        await service.GetAuthorizationContextAsync();

        handler.Paths.Should().Equal(MePath, EntitlementsPath);
        context.User.Id.Should().Be(User);
        context.User.OrganizationId.Should().Be(Organization);
        context.Roles.Should().Equal("Administrator");
    }

    [Fact]
    public async Task APolicy_IsEvaluatedOverTheKernelsPolicyList_FetchedOnce()
    {
        var policies = new[]
        {
            new PolicyDto(Guid.NewGuid(), "reader", "", PolicyOperator.All, [new PolicyRequirementDto(RequirementType.Permission, "serviceorders:read", null)]),
            new PolicyDto(Guid.NewGuid(), "either", "", PolicyOperator.Any, [new PolicyRequirementDto(RequirementType.Role, "Crew", null), new PolicyRequirementDto(RequirementType.Role, "Administrator", null)]),
            new PolicyDto(Guid.NewGuid(), "both", "", PolicyOperator.All, [new PolicyRequirementDto(RequirementType.Role, "Crew", null), new PolicyRequirementDto(RequirementType.Role, "Administrator", null)]),
            new PolicyDto(Guid.NewGuid(), "claimed", "", PolicyOperator.All, [new PolicyRequirementDto(RequirementType.Claim, "tier", "gold")]),
            new PolicyDto(Guid.NewGuid(), "open", "", PolicyOperator.All, []),
        };
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal) ? Json(Me()) : Json(policies));
        var service = new KernelGeoAuthorizationService(Client(handler));

        (await service.EvaluatePolicyAsync("reader")).Should().BeTrue();
        (await service.EvaluatePolicyAsync("either")).Should().BeTrue();
        (await service.EvaluatePolicyAsync("both")).Should().BeFalse();
        (await service.EvaluatePolicyAsync("claimed")).Should().BeTrue();
        (await service.EvaluatePolicyAsync("open")).Should().BeTrue();

        handler.Paths.Where(p => p.EndsWith("/policies", StringComparison.Ordinal)).Should().ContainSingle();
        handler.Paths.Where(p => p.EndsWith("/me", StringComparison.Ordinal)).Should().ContainSingle();
    }

    [Fact]
    public Task AnUnknownPolicy_IsNotFound_NotAGrant()
    {
        var service = new KernelGeoAuthorizationService(Client(new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal) ? Json(Me()) : Json(Array.Empty<PolicyDto>()))));

        var act = async () => await service.EvaluatePolicyAsync("no-such-policy");

        return act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public Task WhenTheKernelRefusesTheCaller_NoContextIsInvented()
    {
        var service = new KernelGeoAuthorizationService(Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        var act = async () => await service.HasPermissionAsync("serviceorders:read");

        return act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public Task AnEmptyAnswer_IsAFailure_NotAnAnonymousContext()
    {
        var service = new KernelGeoAuthorizationService(Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json"),
        })));

        var act = async () => await service.GetAuthorizationContextAsync();

        return act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*empty response*");
    }

    // ── The cross-organization entitlements (XD01-409) ───────────────────────

    [Fact]
    public async Task TheContext_CarriesTheCallersGrantsAndScopedRoles_FromTheKernelsEntitlementsRoute()
    {
        var handler = Kernel();
        var service = new KernelGeoAuthorizationService(Client(handler));

        var context = await service.GetAuthorizationContextAsync();

        var grant = context.OrganizationGrants.Should().ContainSingle().Subject;
        grant.GranteeOrganizationId.Should().Be(Organization);
        grant.ResourceOrganizationId.Should().Be(TestIdentities.OtherOrganization);
        grant.ResourceType.Should().Be("ServiceOrder");
        grant.ScopeId.Should().Be("district-7");
        grant.OrderTypeIds.Should().Equal("cut");
        grant.AllowedActions.Should().Equal("serviceorders:view", "serviceorders:execute");
        grant.RequiredRole.Should().Be("Supervisor");
        grant.RequiresScopedRole.Should().BeTrue();
        grant.IsActive.Should().BeTrue();
        context.ScopedRoles.Should().ContainKey(TestIdentities.OtherOrganization).WhoseValue.Should().Equal("Contractor C1");
        context.ScopedPermissions.Should().ContainKey(TestIdentities.OtherOrganization).WhoseValue.Should().Equal("serviceorders:view", "serviceorders:execute");
        context.Roles.Should().Equal("Administrator");
        context.Permissions.Should().Equal("serviceorders:read");
        context.HasPermission("serviceorders:execute").Should().BeFalse("a permission held on another organization is not a home permission");
    }

    [Fact]
    public async Task ACallerWithNoOrganization_HoldsNoGrant_ButItsScopedRolesStillCount()
    {
        var service = new KernelGeoAuthorizationService(Client(new StubHandler(request =>
            string.Equals(request.RequestUri!.AbsolutePath, EntitlementsPath, StringComparison.Ordinal)
                ? Json(Entitlements())
                : Json(Me() with { OrganizationId = null }))));

        var context = await service.GetAuthorizationContextAsync();

        context.OrganizationGrants.Should().BeEmpty();
        context.ScopedRoles.Should().ContainKey(TestIdentities.OtherOrganization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AnEntitlementsAnswerThatIsNotASuccess_IsNoEntitlement_AndTheHomeContextStands(HttpStatusCode status)
    {
        var service = new KernelGeoAuthorizationService(Client(Kernel(_ => new HttpResponseMessage(status))));

        var context = await service.GetAuthorizationContextAsync();

        context.OrganizationGrants.Should().BeEmpty();
        context.ScopedRoles.Should().BeEmpty();
        context.ScopedPermissions.Should().BeEmpty();
        context.Roles.Should().Equal("Administrator");
    }

    public static TheoryData<string> Failures() => ["unreachable", "timeout", "not-json", "null"];

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task AnEntitlementsRouteThatDoesNotAnswer_IsNoEntitlement_NotAFailedRequest(string failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var service = new KernelGeoAuthorizationService(Client(Kernel(_ => failure switch
        {
            "unreachable" => throw new HttpRequestException("The kernel is unreachable."),
            "timeout" => throw new TaskCanceledException("The request timed out."),
            "not-json" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html/>", Encoding.UTF8, "application/json") },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", Encoding.UTF8, "application/json") },
        })));

        var context = await service.GetAuthorizationContextAsync();

        context.OrganizationGrants.Should().BeEmpty();
        context.ScopedRoles.Should().BeEmpty();
        context.User.Id.Should().Be(User);
    }

    [Fact]
    public async Task TheCallersOwnCancellation_IsNotSwallowed()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new KernelGeoAuthorizationService(Client(Kernel()));
        (await service.HasPermissionAsync("serviceorders:read")).Should().BeTrue();
        await cancellation.CancelAsync();

        var act = async () => await service.GetAuthorizationContextAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a cancelled request is not an unavailable kernel");
    }

    [Fact]
    public Task AKernelThatRejectsTheForwardedToken_OnTheEntitlementsRoute_IsStillATokenRejection()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Request.Headers.Authorization = "Bearer caller-token";
        var forwarding = new ForwardedBearerTokenHandler(accessor)
        {
            InnerHandler = Kernel(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)),
        };
        var service = new KernelGeoAuthorizationService(new HttpClient(forwarding) { BaseAddress = new Uri("http://kernel.test/api/identity/") });

        var act = async () => await service.GetAuthorizationContextAsync();

        return act.Should().ThrowAsync<KernelRejectedCallerTokenException>();
    }

    // ── KernelOwnOrganizationGrantSource ──────────────────────────────────────

    [Fact]
    public async Task TheCallersOrganizationsGrants_AreTheContextsGrants_ReadWithTheEntitlementsOnce()
    {
        var handler = Kernel();
        var service = new KernelGeoAuthorizationService(Client(handler));
        var source = new KernelOwnOrganizationGrantSource(service);

        var grants = await source.GetActiveGrantsForGranteeAsync(Organization, CancellationToken.None);
        await source.GetActiveGrantsForGranteeAsync(Organization, CancellationToken.None);
        await service.GetAuthorizationContextAsync();

        grants.Should().ContainSingle().Which.ResourceOrganizationId.Should().Be(TestIdentities.OtherOrganization);
        handler.Paths.Should().BeEquivalentTo([MePath, EntitlementsPath], "each kernel route is read once, and no separate grant route");
    }

    [Fact]
    public async Task AnotherOrganizationsGrants_AreNone()
    {
        var source = new KernelOwnOrganizationGrantSource(new KernelGeoAuthorizationService(Client(Kernel())));

        (await source.GetActiveGrantsForGranteeAsync(TestIdentities.OtherOrganization, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheKernelDoesNotAnswerTheEntitlements_ThereAreNoGrants_SoALessGenerousGateNeverAMoreGenerousOne()
    {
        var source = new KernelOwnOrganizationGrantSource(
            new KernelGeoAuthorizationService(Client(Kernel(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))));

        (await source.GetActiveGrantsForGranteeAsync(Organization, CancellationToken.None)).Should().BeEmpty();
    }
}
