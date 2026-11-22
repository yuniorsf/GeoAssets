using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>
/// XD01-332 — the client learns its own organization's active grants, so the service-order organization gate evaluates in
/// the browser as it does on the server. Since XD01-329 they come with the roles and permissions held on other
/// organizations from <c>GET me/cross-org-entitlements</c>, which replaced <c>GET organization-grants/me</c> here.
/// </summary>
public class RestGeoAuthorizationServiceOrganizationGrantsTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000332");
    private static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000332");

    private static AuthorizationContextDto Me(Guid? organizationId) => new(
        Id: Guid.NewGuid(), Email: "user@example.com", DisplayName: "Test User", OrganizationId: organizationId,
        Roles: ["Supervisor"], Claims: [], Permissions: ["serviceorders:read"]);

    private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: _opts) };

    private static RestGeoAuthorizationService Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/api/identity/") });

    private static bool IsEntitlementsRoute(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/me/cross-org-entitlements", StringComparison.Ordinal);

    private static CrossOrgEntitlementsDto Entitlements(params CrossOrgGrantEntitlementDto[] grants) => new(grants,
        [new ScopedRoleEntitlementDto(OrgA, "Org A", ["Contractor C1"], ["serviceorders:view", "serviceorders:execute"])]);

    private static CrossOrgGrantEntitlementDto Grant(DateTime? expiresAt = null) => new(
        OrgA, "Org A", "ServiceOrder", "municipio-123", ["cut"], ["serviceorders:view"], "Supervisor", RequiresScopedRole: true, expiresAt);

    [Fact]
    public async Task XD01_332_GetAuthorizationContextAsync_CallerWithAnOrganization_CarriesItsOwnOrganizationsGrants()
    {
        var expiresAt = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new FakeHttpMessageHandler(request => IsEntitlementsRoute(request) ? Json(Entitlements(Grant(expiresAt))) : Json(Me(OrgB)));

        var context = await Sut(handler).GetAuthorizationContextAsync();

        var grant = context.OrganizationGrants.Should().ContainSingle().Subject;
        grant.GranteeOrganizationId.Should().Be(OrgB);
        grant.ResourceOrganizationId.Should().Be(OrgA);
        grant.ResourceType.Should().Be("ServiceOrder");
        grant.ScopeId.Should().Be("municipio-123");
        grant.AllowedActions.Should().Equal("serviceorders:view");
        grant.RequiredRole.Should().Be("Supervisor");
        grant.RequiresScopedRole.Should().BeTrue();
        grant.OrderTypeIds.Should().Equal("cut");
        grant.ExpiresAt.Should().Be(expiresAt);
        grant.IsActive.Should().BeTrue();
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().Equal("/api/identity/me", "/api/identity/me/cross-org-entitlements");
        handler.Requests.Should().OnlyContain(r => string.IsNullOrEmpty(r.RequestUri!.Query), "the server derives the caller from its token");
    }

    [Fact]
    public async Task XD01_329_GetAuthorizationContextAsync_CarriesTheRolesAndPermissionsHeldOnOtherOrganizations()
    {
        var handler = new FakeHttpMessageHandler(request => IsEntitlementsRoute(request) ? Json(Entitlements()) : Json(Me(OrgB)));

        var context = await Sut(handler).GetAuthorizationContextAsync();

        context.ScopedRoles[OrgA].Should().Equal("Contractor C1");
        context.ScopedPermissions[OrgA].Should().Equal("serviceorders:view", "serviceorders:execute");
        context.HasPermissionOn(OrgA, "serviceorders:execute").Should().BeTrue();
        context.HasPermission("serviceorders:execute").Should().BeFalse("a permission held on another organization is never a home one");
        context.Roles.Should().Equal("Supervisor");
    }

    [Fact]
    public async Task XD01_332_GetAuthorizationContextAsync_CallerWithNoOrganization_HoldsNoGrants_ButItsScopedRolesStillCount()
    {
        var handler = new FakeHttpMessageHandler(request => IsEntitlementsRoute(request) ? Json(Entitlements(Grant())) : Json(Me(organizationId: null)));

        var context = await Sut(handler).GetAuthorizationContextAsync();

        context.OrganizationGrants.Should().BeEmpty();
        context.ScopedPermissions.Should().ContainKey(OrgA);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task XD01_332_GetAuthorizationContextAsync_ServerThatDoesNotAnswerTheEntitlementsRoute_YieldsNone_NotAFailure(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler(request => IsEntitlementsRoute(request) ? new HttpResponseMessage(status) : Json(Me(OrgB)));

        var context = await Sut(handler).GetAuthorizationContextAsync();

        context.OrganizationGrants.Should().BeEmpty();
        context.ScopedRoles.Should().BeEmpty();
        context.ScopedPermissions.Should().BeEmpty();
        context.Roles.Should().Equal("Supervisor");
    }

    [Fact]
    public async Task XD01_332_GetAuthorizationContextAsync_IsFetchedOncePerInstance_EntitlementsIncluded()
    {
        var handler = new FakeHttpMessageHandler(request => IsEntitlementsRoute(request) ? Json(CrossOrgEntitlementsDto.None) : Json(Me(OrgB)));
        var sut = Sut(handler);

        var first = await sut.GetAuthorizationContextAsync();
        var second = await sut.GetAuthorizationContextAsync();

        handler.Requests.Should().HaveCount(2);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task XD01_329_AfterTheEntitlementsAreRefreshed_TheContextCarriesTheNewOnes_WithoutFetchingMeAgain()
    {
        var revoked = false;
        var handler = new FakeHttpMessageHandler(request =>
            IsEntitlementsRoute(request) ? Json(revoked ? CrossOrgEntitlementsDto.None : Entitlements(Grant())) : Json(Me(OrgB)));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://test/api/identity/") };
        var entitlements = new RestCrossOrgEntitlementsService(http);
        var sut = new RestGeoAuthorizationService(http, entitlements);

        (await sut.GetAuthorizationContextAsync()).OrganizationGrants.Should().ContainSingle();
        revoked = true;
        await entitlements.RefreshAsync();
        var refreshed = await sut.GetAuthorizationContextAsync();

        refreshed.OrganizationGrants.Should().BeEmpty();
        refreshed.ScopedPermissions.Should().BeEmpty();
        handler.Requests.Count(r => string.Equals(r.RequestUri!.AbsolutePath, "/api/identity/me", StringComparison.Ordinal)).Should().Be(1);
    }
}
