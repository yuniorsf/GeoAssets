using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Hosting;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// This host has no identity database (XD01-382, ADR-001 D3): who the caller is, what it may do and which grants its organization holds are the
/// kernel's answers (<see cref="KernelGeoAuthorizationService"/>, <see cref="KernelOwnOrganizationGrantSource"/>) — read at most once per
/// request, and never more generous than the kernel.
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

    // ── KernelGeoAuthorizationService ─────────────────────────────────────────

    [Fact]
    public async Task TheCallersContext_IsReadFromTheKernelsMeRoute_OnceForAllTheQuestionsOfARequest()
    {
        var handler = new StubHandler(_ => Json(Me()));
        var service = new KernelGeoAuthorizationService(Client(handler));

        (await service.IsInRoleAsync("Administrator")).Should().BeTrue();
        (await service.IsInRoleAsync("Crew")).Should().BeFalse();
        (await service.HasPermissionAsync("serviceorders:read")).Should().BeTrue();
        (await service.HasPermissionAsync("serviceorders:delete")).Should().BeFalse();
        (await service.HasClaimAsync("tier", "gold")).Should().BeTrue();
        (await service.HasClaimAsync("tier", "silver")).Should().BeFalse();
        var context = await service.GetAuthorizationContextAsync();

        handler.Paths.Should().Equal("/api/identity/me");
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

    // ── KernelOwnOrganizationGrantSource ──────────────────────────────────────

    [Fact]
    public async Task TheCallersOrganizationsGrants_AreReadFromTheKernel_AsActiveGrantsHeldByThatOrganization()
    {
        var handler = new StubHandler(_ => Json(new[]
        {
            new OwnOrganizationGrantDto(TestIdentities.OtherOrganization, "ServiceOrder", "district-7", ["View", "Annotate"], "Supervisor", null),
        }));

        var grants = await new KernelOwnOrganizationGrantSource(Client(handler)).GetActiveGrantsForGranteeAsync(Organization, CancellationToken.None);

        handler.Paths.Should().Equal("/api/identity/organization-grants/me");
        var grant = grants.Should().ContainSingle().Subject;
        grant.GranteeOrganizationId.Should().Be(Organization);
        grant.ResourceOrganizationId.Should().Be(TestIdentities.OtherOrganization);
        grant.ResourceType.Should().Be("ServiceOrder");
        grant.ScopeId.Should().Be("district-7");
        grant.AllowedActions.Should().Equal("View", "Annotate");
        grant.RequiredRole.Should().Be("Supervisor");
        grant.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task WhenTheKernelDoesNotAnswer_ThereAreNoGrants_SoALessGenerousGateNeverAMoreGenerousOne(HttpStatusCode status)
    {
        var source = new KernelOwnOrganizationGrantSource(Client(new StubHandler(_ => new HttpResponseMessage(status))));

        (await source.GetActiveGrantsForGranteeAsync(Organization, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task ANullBody_IsNoGrants()
    {
        var source = new KernelOwnOrganizationGrantSource(Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json"),
        })));

        (await source.GetActiveGrantsForGranteeAsync(Organization, CancellationToken.None)).Should().BeEmpty();
    }
}
