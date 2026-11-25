using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.CrossOrgEntitlementsParity;
using GeoAssets.Identity.Authorization.Services;
using Xunit;
using static GeoAssets.CrossOrgEntitlementsParity.CrossOrgEntitlementsParityMatrix;

namespace GeoAssets.Server.Tests.CrossOrgEntitlements;

/// <summary>
/// XD01-409 — the vertical-host half of <see cref="CrossOrgEntitlementsParityMatrix"/>: a host without the identity database
/// (NeutralOperator.Server, IpBilling.Server) builds its principal with <see cref="ServerWorkflowPrincipalFactory"/> over
/// <see cref="KernelGeoAuthorizationService"/> and <see cref="KernelOwnOrganizationGrantSource"/> — what
/// <c>AddKernelIdentity</c> registers — fed the <c>/me</c> and <c>/me/cross-org-entitlements</c> answers the kernel gives for
/// each case's identity data, and reaches the decision the matrix states, the one the kernel and the Web client reach.
/// </summary>
public sealed class VerticalHostCrossOrgEntitlementsParityTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private sealed class KernelIdentityRoutes(AuthorizationContextDto me, CrossOrgEntitlementsDto entitlements) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith("/me/cross-org-entitlements", StringComparison.Ordinal)
                    ? JsonContent.Create(entitlements, options: Wire)
                    : JsonContent.Create(me, options: Wire),
            });
    }

    [Theory]
    [MemberData(nameof(CaseNames), MemberType = typeof(CrossOrgEntitlementsParityMatrix))]
    public async Task XD01_409_AVerticalHostDecidesEachCaseAsTheMatrixStates(string name)
    {
        var row = Get(name);
        var context = row.Context;
        var me = new AuthorizationContextDto(
            context.User.Id, context.User.Email, context.User.DisplayName, context.User.OrganizationId, context.Roles, [], context.Permissions);
        var kernel = new KernelIdentityRoutes(me, CrossOrgEntitlementsDto.Create(context, row.ActiveGrants, OrganizationNames));
        var authorization = new KernelGeoAuthorizationService(new HttpClient(kernel) { BaseAddress = new Uri("http://kernel.test/api/identity/") });

        var principal = await new ServerWorkflowPrincipalFactory(authorization, new KernelOwnOrganizationGrantSource(authorization)).CreateAsync();

        Rules().Evaluate(principal, row.Action, row.Order).Allowed.Should().Be(row.Allowed, row.Name);
    }
}
