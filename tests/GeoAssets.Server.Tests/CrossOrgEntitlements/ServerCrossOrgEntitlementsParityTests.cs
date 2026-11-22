using FluentAssertions;
using GeoAssets.CrossOrgEntitlementsParity;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Xunit;
using static GeoAssets.CrossOrgEntitlementsParity.CrossOrgEntitlementsParityMatrix;

namespace GeoAssets.Server.Tests.CrossOrgEntitlements;

/// <summary>
/// XD01-329 — the server half of <see cref="CrossOrgEntitlementsParityMatrix"/>: the kernel's
/// <see cref="ServerWorkflowPrincipalFactory"/>, over each case's identity data, reaches the decision the matrix states.
/// <c>GeoAssets.Web.Tests</c>' <c>WebCrossOrgEntitlementsParityTests</c> asserts the same rows for the Web client.
/// </summary>
public sealed class ServerCrossOrgEntitlementsParityTests
{
    private sealed class FixedAuthorizationService(AuthorizationContext context) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(context);
    }

    [Theory]
    [MemberData(nameof(CaseNames), MemberType = typeof(CrossOrgEntitlementsParityMatrix))]
    public async Task XD01_329_TheKernelDecidesEachCaseAsTheMatrixStates(string name)
    {
        var row = Get(name);
        var grants = new TestOrganizationGrantRepository();
        grants.Active.AddRange(row.ActiveGrants);

        var principal = await new ServerWorkflowPrincipalFactory(new FixedAuthorizationService(row.Context), grants).CreateAsync();

        Rules().Evaluate(principal, row.Action, row.Order).Allowed.Should().Be(row.Allowed, row.Name);
    }
}
