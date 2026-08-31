using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Server.Tests;

public class GeoAuthorizationHandlerTests
{
    private sealed class FakeAuthorizationService(Func<string, bool> evaluate) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default)
            => Task.FromResult(evaluate(policyName));

        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static GeoAuthorizationHandler Sut(Func<string, bool> evaluate) =>
        new(new FakeAuthorizationService(evaluate), NullLogger<GeoAuthorizationHandler>.Instance);

    // Authenticated by default — these tests exercise the requirement's own policy-evaluation
    // logic, not the authentication guard (covered separately below).
    private static AuthorizationHandlerContext Context(GeoPolicyRequirement requirement) =>
        new([requirement], new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "TestAuth")), resource: null);

    [Fact]
    public async Task HandleRequirementAsync_PolicySatisfied_Succeeds()
    {
        var handler = Sut(_ => true);
        var requirement = new GeoPolicyRequirement("CanEditFeatures");
        var context = Context(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_PolicyNotSatisfied_DoesNotSucceed()
    {
        // Non-leakage: the requirement must not be granted just because the handler ran —
        // it must actually reflect what IGeoAuthorizationService reported.
        var handler = Sut(_ => false);
        var requirement = new GeoPolicyRequirement("CanManageUsers");
        var context = Context(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_UnknownPolicyName_FailsClosedWithoutThrowing()
    {
        var handler = Sut(name => throw new KeyNotFoundException($"Policy '{name}' not found."));
        var requirement = new GeoPolicyRequirement("DoesNotExist");
        var context = Context(requirement);

        var act = () => handler.HandleAsync(context);

        await act.Should().NotThrowAsync();
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_AnonymousCaller_DeniesWithoutCallingTheAuthorizationService()
    {
        // Regression: GeoAuthorizationPolicyProvider pairs every named policy with
        // RequireAuthenticatedUser(), but ASP.NET Core still runs this handler even when that
        // paired requirement hasn't succeeded — the production IGeoAuthorizationService throws
        // for an anonymous caller instead of returning false, so this must never call it. Fails
        // without the fix (the fake would throw, and HandleAsync would propagate that instead of
        // denying cleanly).
        var handler = Sut(_ => throw new InvalidOperationException(
            "Must not evaluate the policy for an anonymous caller."));
        var requirement = new GeoPolicyRequirement("CanEditFeatures");
        var context = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(new ClaimsIdentity()), resource: null);

        var act = () => handler.HandleAsync(context);

        await act.Should().NotThrowAsync();
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_EvaluatesTheRequirementsOwnPolicyName_NotAnyOther()
    {
        // Non-leakage: a handler that granted based on the wrong policy name would pass a
        // caller with permission for "CanExportReports" through a "CanManageUsers" gate.
        var handler = Sut(name => name == "CanExportReports");
        var requirement = new GeoPolicyRequirement("CanManageUsers");
        var context = Context(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
