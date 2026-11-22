using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>XD01-327: <see cref="RestScopedRoleAdministrationClient"/> against the scoped-role administration routes.</summary>
public sealed class RestScopedRoleAdministrationClientTests
{
    private static readonly Guid Org = new("c0e1ba00-0000-4000-8000-000000000327");
    private static readonly Guid User = new("bb000002-0000-4000-8000-000000000327");
    private static readonly Guid Role = new("13000000-0000-4000-8000-000000000327");

    private static RestScopedRoleAdministrationClient Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/api/identity/") });

    private static ScopedRoleAssignmentDto Assignment() => new(
        Guid.NewGuid(), User, "Tiago", "tiago@bimbo.test", Guid.NewGuid(), "BIMBO", Org, "COELBA", Role, "Contractor C1",
        "admin@coelba.test", new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc));

    private static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

    [Fact]
    public async Task AssignAsync_PostsTheUserAndRole_ToTheOrganizationsRoute_AndReadsTheAssignment()
    {
        var dto = Assignment();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(dto) });

        var assigned = await Sut(handler).AssignAsync(Org, User, Role);

        assigned.Should().Be(dto);
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be($"/api/identity/organizations/{Org}/scoped-roles");
        (await request.Content!.ReadFromJsonAsync<ScopedRoleAssignRequest>()).Should().Be(new ScopedRoleAssignRequest(User, Role));
    }

    [Fact]
    public async Task AssignAsync_ARefusal_RaisesItsStatusCodeAndDetail()
    {
        var handler = new FakeHttpMessageHandler(_ => Problem(HttpStatusCode.Conflict,
            """{"status":409,"detail":"The user's organization holds no active grant from this organization.","code":"scoped-role.not-contracted"}"""));

        var act = () => Sut(handler).AssignAsync(Org, User, Role);

        var refusal = (await act.Should().ThrowAsync<ScopedRoleAdministrationException>()).Which;
        refusal.StatusCode.Should().Be(409);
        refusal.Code.Should().Be(ScopedRoleAssignmentProblems.NotContracted);
        refusal.Message.Should().Be("The user's organization holds no active grant from this organization.");
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("")]
    [InlineData("[1,2]")]
    [InlineData("""{"code":7}""")]
    public async Task ARefusalWithoutAProblemBody_StillRaisesItsStatus_WithNoCode(string body)
    {
        var handler = new FakeHttpMessageHandler(_ => Problem(HttpStatusCode.BadGateway, body));

        var act = () => Sut(handler).GetAssignmentsAsync(Org);

        var refusal = (await act.Should().ThrowAsync<ScopedRoleAdministrationException>()).Which;
        refusal.StatusCode.Should().Be(502);
        refusal.Code.Should().BeNull();
        refusal.Message.Should().Contain("502");
    }

    [Fact]
    public async Task RevokeAsync_DeletesTheAssignment_AndAForbiddenAnswerRaises403()
    {
        var assignmentId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(request => request.Method == HttpMethod.Delete && request.RequestUri!.AbsolutePath.EndsWith($"/{assignmentId}", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : Problem(HttpStatusCode.Forbidden, """{"code":"scoped-role.forbidden"}"""));
        var sut = Sut(handler);

        await sut.RevokeAsync(Org, assignmentId);
        var act = () => sut.RevokeAsync(Org, Guid.NewGuid());

        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be($"/api/identity/organizations/{Org}/scoped-roles/{assignmentId}");
        (await act.Should().ThrowAsync<ScopedRoleAdministrationException>()).Which.Code.Should().Be(ScopedRoleAssignmentProblems.Forbidden);
    }

    [Fact]
    public async Task TheReads_GetTheirRoutes_AndReadTheirLists()
    {
        var assignment = Assignment();
        var contracted = new ContractedUserDto(User, "Tiago", "tiago@bimbo.test", Guid.NewGuid(), "BIMBO");
        var grant = new OrganizationGrantDto(Guid.NewGuid(), Guid.NewGuid(), Org, "ServiceOrder", null, ["serviceorders:read"], null, null, "seed",
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), true) { RequiresScopedRole = true, OrderTypeIds = ["cut"] };
        var handler = new FakeHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/contracted-users", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { contracted }) },
            var path when path.EndsWith("/issued-grants", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { grant }) },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { assignment }) },
        });
        var sut = Sut(handler);

        (await sut.GetAssignmentsAsync(Org)).Should().ContainSingle().Which.Should().Be(assignment);
        (await sut.GetContractedUsersAsync(Org)).Should().ContainSingle().Which.Should().Be(contracted);
        var issued = (await sut.GetIssuedGrantsAsync(Org)).Should().ContainSingle().Subject;
        issued.RequiresScopedRole.Should().BeTrue();
        issued.OrderTypeIds.Should().Equal("cut");
        (await sut.GetUserAssignmentsAsync(User)).Should().ContainSingle().Which.Id.Should().Be(assignment.Id);

        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).Should().Equal(
            $"/api/identity/organizations/{Org}/scoped-roles",
            $"/api/identity/organizations/{Org}/contracted-users",
            $"/api/identity/organizations/{Org}/issued-grants",
            $"/api/identity/users/{User}/scoped-roles");
    }
}
