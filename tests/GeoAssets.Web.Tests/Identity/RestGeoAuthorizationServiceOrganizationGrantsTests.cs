using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>
/// XD01-332 — the client learns its own organization's active grants from
/// <c>GET organization-grants/me</c>, so the service-order organization gate evaluates in the
/// browser as it does on the server.
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

    private static bool IsGrantsRoute(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/organization-grants/me", StringComparison.Ordinal);

    [Fact]
    public async Task XD01_332_GetAuthorizationContextAsync_CallerWithAnOrganization_CarriesItsOwnOrganizationsGrants()
    {
        var expiresAt = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        OwnOrganizationGrantDto[] grants = [new(OrgA, "ServiceOrder", "municipio-123", ["serviceorders:view"], "Supervisor", expiresAt)];
        var handler = new FakeHttpMessageHandler(request => IsGrantsRoute(request) ? Json(grants) : Json(Me(OrgB)));

        var context = await Sut(handler).GetAuthorizationContextAsync();

        var grant = context.OrganizationGrants.Should().ContainSingle().Subject;
        grant.GranteeOrganizationId.Should().Be(OrgB);
        grant.ResourceOrganizationId.Should().Be(OrgA);
        grant.ResourceType.Should().Be("ServiceOrder");
        grant.ScopeId.Should().Be("municipio-123");
        grant.AllowedActions.Should().Equal("serviceorders:view");
        grant.RequiredRole.Should().Be("Supervisor");
        grant.ExpiresAt.Should().Be(expiresAt);
        grant.IsActive.Should().BeTrue();
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().Equal("/api/identity/me", "/api/identity/organization-grants/me");
        handler.Requests.Should().OnlyContain(r => string.IsNullOrEmpty(r.RequestUri!.Query), "the server derives the organization from the caller");
    }

    [Fact]
    public async Task XD01_332_GetAuthorizationContextAsync_CallerWithNoOrganization_HoldsNoGrants_AndAsksForNone()
    {
        var handler = new FakeHttpMessageHandler(_ => Json(Me(organizationId: null)));

        var context = await Sut(handler).GetAuthorizationContextAsync();

        context.OrganizationGrants.Should().BeEmpty();
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task XD01_332_GetAuthorizationContextAsync_ServerThatDoesNotAnswerTheGrantsRoute_YieldsNoGrants_NotAFailure(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler(request => IsGrantsRoute(request) ? new HttpResponseMessage(status) : Json(Me(OrgB)));

        var context = await Sut(handler).GetAuthorizationContextAsync();

        context.OrganizationGrants.Should().BeEmpty();
        context.Roles.Should().Equal("Supervisor");
    }

    [Fact]
    public async Task XD01_332_GetAuthorizationContextAsync_IsFetchedOncePerInstance_GrantsIncluded()
    {
        var handler = new FakeHttpMessageHandler(request => IsGrantsRoute(request) ? Json(Array.Empty<OwnOrganizationGrantDto>()) : Json(Me(OrgB)));
        var sut = Sut(handler);

        await sut.GetAuthorizationContextAsync();
        await sut.GetAuthorizationContextAsync();

        handler.Requests.Should().HaveCount(2);
    }
}
