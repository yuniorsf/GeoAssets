using FluentAssertions;
using GeoAssets.CrossOrgEntitlementsParity;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
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

    /// <summary>
    /// XD01-408 — the kernel's list holds a row's order exactly when the same caller can read it by id (the organization gate,
    /// <c>ServiceOrderRouteAccess.FindReadableAsync</c>): another organization's order reaches the list only through the
    /// grant-aware read path, the caller's own through its tenant-filtered list.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseNames), MemberType = typeof(CrossOrgEntitlementsParityMatrix))]
    public async Task XD01_408_TheKernelListsARowsOrder_ExactlyWhenTheCallerCanReadItById(string name)
    {
        var row = Get(name);
        var grants = new TestOrganizationGrantRepository();
        grants.Active.AddRange(row.ActiveGrants);
        var principal = await new ServerWorkflowPrincipalFactory(new FixedAuthorizationService(row.Context), grants).CreateAsync();
        var rules = Rules();
        var isOwn = row.Order.OrganizationId == Bimbo;

        var listed = await GrantedServiceOrderList.ComposeAsync(
            isOwn ? [row.Order] : [], new OtherOrganizations(isOwn ? [] : [row.Order]), rules, principal, CancellationToken.None);

        listed.Contains(row.Order).Should().Be(rules.PassesOrganizationGate(principal, row.Order), row.Name);
    }

    [Fact]
    public async Task XD01_408_TheEntitledRowsAreListed_AndTheOthersAreNot()
    {
        var expected = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["entitled order type: execute"] = true,
            ["entitled order type: view"] = true,
            ["entitled order type: an action the grant does not list"] = true,
            ["other order type"] = false,
            ["out-of-scope order"] = false,
            ["no scoped role, the permission held at home"] = false,
            ["revoked grant"] = false,
            ["no cross-organization entitlement, own organization's order"] = true,
        };

        foreach (var row in Cases)
        {
            var grants = new TestOrganizationGrantRepository();
            grants.Active.AddRange(row.ActiveGrants);
            var principal = await new ServerWorkflowPrincipalFactory(new FixedAuthorizationService(row.Context), grants).CreateAsync();
            var isOwn = row.Order.OrganizationId == Bimbo;

            var listed = await GrantedServiceOrderList.ComposeAsync(
                isOwn ? [row.Order] : [], new OtherOrganizations(isOwn ? [] : [row.Order]), Rules(), principal, CancellationToken.None);

            listed.Contains(row.Order).Should().Be(expected[row.Name], row.Name);
        }
    }

    /// <summary>The grant-aware read path over a fixed set of other organizations' orders.</summary>
    private sealed class OtherOrganizations(IReadOnlyList<IServiceOrder> orders) : ICrossOrganizationServiceOrderReader
    {
        public Task<IReadOnlyList<IServiceOrder>> GetOfOtherOrganizationsAsync(IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. orders.Where(o => organizationIds.Contains(o.OrganizationId))]);
    }
}
