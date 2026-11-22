using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.CrossOrgEntitlementsParity;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;
using static GeoAssets.CrossOrgEntitlementsParity.CrossOrgEntitlementsParityMatrix;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>
/// XD01-329 — the client half of <see cref="CrossOrgEntitlementsParityMatrix"/>: the Web client's
/// <see cref="RestGeoAuthorizationService"/> and <see cref="WorkflowPrincipalFactory"/>, fed the <c>/me</c> and
/// <c>/me/cross-org-entitlements</c> answers the kernel gives for each case's identity data (the latter built by
/// <see cref="CrossOrgEntitlementsDto.Create"/>, which the kernel's endpoint uses), reach the decision the matrix states —
/// the one <c>GeoAssets.Server.Tests</c>' <c>ServerCrossOrgEntitlementsParityTests</c> asserts for the kernel.
/// </summary>
public sealed class WebCrossOrgEntitlementsParityTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: _opts) };

    [Theory]
    [MemberData(nameof(CaseNames), MemberType = typeof(CrossOrgEntitlementsParityMatrix))]
    public async Task XD01_329_TheWebClientDecidesEachCaseAsTheMatrixStates(string name)
    {
        var row = Get(name);
        var context = row.Context;
        var me = new AuthorizationContextDto(
            context.User.Id, context.User.Email, context.User.DisplayName, context.User.OrganizationId,
            context.Roles, [], context.Permissions);
        var entitlements = CrossOrgEntitlementsDto.Create(context, row.ActiveGrants, OrganizationNames);
        var handler = new FakeHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/me/cross-org-entitlements", StringComparison.Ordinal) ? Json(entitlements) : Json(me));
        var authorization = new RestGeoAuthorizationService(new HttpClient(handler) { BaseAddress = new Uri("http://test/api/identity/") });

        var principal = await new WorkflowPrincipalFactory(authorization).CreateAsync();

        Rules().Evaluate(principal, row.Action, row.Order).Allowed.Should().Be(row.Allowed, row.Name);
    }
}
