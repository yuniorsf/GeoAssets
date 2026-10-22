using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace GeoAssets.Server.Tests;

public class RequireOrganizationAuthorizationHandlerTests
{
    private sealed class FakeAuthorizationService(Func<AuthorizationContext> resolve) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
            => Task.FromResult(resolve());
    }

    private static AuthorizationContext CtxFor(Guid? organizationId) => new()
    {
        User = new AppUser
        {
            ExternalObjectId = "oid",
            CreatedAt = DateTime.UtcNow,
            OrganizationId = organizationId,
        },
        Roles = [],
        Claims = [],
        Permissions = [],
    };

    private static RequireOrganizationAuthorizationHandler Sut(Func<AuthorizationContext> resolve) =>
        new(new FakeAuthorizationService(resolve));

    // Authenticated by default — these tests exercise the requirement's own
    // organization-binding check, not the authentication guard (covered separately below).
    private static AuthorizationHandlerContext Context(RequireOrganizationRequirement requirement) =>
        new([requirement], new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "TestAuth")), resource: null);

    [Fact]
    public async Task HandleRequirementAsync_OrganizationAssigned_Succeeds()
    {
        var handler = Sut(() => CtxFor(Guid.NewGuid()));
        var requirement = new RequireOrganizationRequirement();
        var context = Context(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_OrganizationNull_DoesNotSucceed()
    {
        // Covers both "just JIT-provisioned on this very request" and "still unassigned on
        // a later request" — identical from this handler's point of view, by design: the
        // first request that provisions the AppUser row is allowed to complete regardless
        // (this handler isn't consulted on the auth/token path), and only a request that
        // actually reaches an endpoint gated by this requirement gets denied.
        var handler = Sut(() => CtxFor(null));
        var requirement = new RequireOrganizationRequirement();
        var context = Context(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_AnonymousCaller_DeniesWithoutCallingTheAuthorizationService()
    {
        // Regression: ASP.NET Core still runs this handler even when a paired
        // RequireAuthenticatedUser() requirement hasn't succeeded — the production
        // IGeoAuthorizationService throws for an anonymous caller instead of returning
        // false, so this must never call it. Fails without the guard (the fake would throw,
        // and HandleAsync would propagate that instead of denying cleanly).
        var handler = Sut(() => throw new InvalidOperationException(
            "Must not resolve the authorization context for an anonymous caller."));
        var requirement = new RequireOrganizationRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(new ClaimsIdentity()), resource: null);

        var act = () => handler.HandleAsync(context);

        await act.Should().NotThrowAsync();
        context.HasSucceeded.Should().BeFalse();
    }
}
