using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

public class RestGeoAuthorizationServiceTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly AuthorizationContextDto _meWithFeaturesRead = new(
        Id: Guid.NewGuid(), Email: "user@example.com", DisplayName: "Test User", OrganizationId: null,
        Roles: ["Supervisor"], Claims: [new ClaimDto("zone", "north")], Permissions: ["features:read"]);

    private static RestGeoAuthorizationService Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse<T>(T body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: _opts) };

    [Fact]
    public async Task GetAuthorizationContextAsync_MapsAllFieldsFromMeEndpoint()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(_meWithFeaturesRead));
        var sut = Sut(handler);

        var ctx = await sut.GetAuthorizationContextAsync();

        ctx.User.Id.Should().Be(_meWithFeaturesRead.Id);
        ctx.User.Email.Should().Be("user@example.com");
        ctx.Roles.Should().BeEquivalentTo(["Supervisor"]);
        ctx.Claims.Should().ContainSingle(c => c.Type == "zone" && c.Value == "north");
        ctx.Permissions.Should().BeEquivalentTo(["features:read"]);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/me");
    }

    [Fact]
    public async Task HasPermissionAsync_PermissionGranted_ReturnsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(_meWithFeaturesRead));
        var sut = Sut(handler);

        (await sut.HasPermissionAsync("features:read")).Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_PermissionNotGranted_ReturnsFalse()
    {
        // Non-leakage: a permission absent from the server's response must not be
        // treated as granted just because some other permission is present.
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(_meWithFeaturesRead));
        var sut = Sut(handler);

        (await sut.HasPermissionAsync("users:manage")).Should().BeFalse();
    }

    [Fact]
    public async Task GetAuthorizationContextAsync_CalledMultipleTimes_FetchesOnlyOnce()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(_meWithFeaturesRead));
        var sut = Sut(handler);

        await sut.GetAuthorizationContextAsync();
        await sut.HasPermissionAsync("features:read");
        await sut.IsInRoleAsync("Supervisor");

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task EvaluatePolicyAsync_ByName_AllOperator_RequiresEveryRequirement()
    {
        var policy = new PolicyDto(
            Id: Guid.NewGuid(), Name: "CanEditFeatures", Description: "", Operator: PolicyOperator.All,
            Requirements: [
                new PolicyRequirementDto(RequirementType.Permission, "features:read", null),
                new PolicyRequirementDto(RequirementType.Permission, "features:edit", null),
            ]);

        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/me"       => JsonResponse(_meWithFeaturesRead), // only has features:read, not features:edit
            "/policies" => JsonResponse(new List<PolicyDto> { policy }),
            _           => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var sut = Sut(handler);

        // Non-leakage: missing just one of two "All" requirements must deny, not grant.
        (await sut.EvaluatePolicyAsync("CanEditFeatures")).Should().BeFalse();
    }

    [Fact]
    public async Task EvaluatePolicyAsync_ByName_AllRequirementsMet_ReturnsTrue()
    {
        var policy = new PolicyDto(
            Id: Guid.NewGuid(), Name: "CanReadFeatures", Description: "", Operator: PolicyOperator.All,
            Requirements: [new PolicyRequirementDto(RequirementType.Permission, "features:read", null)]);

        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/me"       => JsonResponse(_meWithFeaturesRead),
            "/policies" => JsonResponse(new List<PolicyDto> { policy }),
            _           => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var sut = Sut(handler);

        (await sut.EvaluatePolicyAsync("CanReadFeatures")).Should().BeTrue();
    }

    [Fact]
    public async Task EvaluatePolicyAsync_UnknownPolicyName_ThrowsKeyNotFoundException()
    {
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/policies" => JsonResponse(new List<PolicyDto>()),
            _           => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var sut = Sut(handler);

        var act = () => sut.EvaluatePolicyAsync("DoesNotExist");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
