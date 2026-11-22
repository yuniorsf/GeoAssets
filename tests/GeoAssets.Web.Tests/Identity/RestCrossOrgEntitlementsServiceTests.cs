using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>
/// XD01-329 — <see cref="RestCrossOrgEntitlementsService"/>: <c>GET me/cross-org-entitlements</c>, fetched once, again only on
/// <see cref="RestCrossOrgEntitlementsService.RefreshAsync"/>, and <see cref="CrossOrgEntitlementsDto.None"/> from a server
/// that does not answer.
/// </summary>
public sealed class RestCrossOrgEntitlementsServiceTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000329");

    private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: _opts) };

    private static RestCrossOrgEntitlementsService Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/api/identity/") });

    private static CrossOrgEntitlementsDto Named(string name) =>
        new([], [new ScopedRoleEntitlementDto(Coelba, name, ["Contractor C1"], ["serviceorders:view"])]);

    [Fact]
    public async Task GetAsync_ReadsTheCallersEntitlements_FromTheRoute_WithNothingTheClientChooses()
    {
        var handler = new FakeHttpMessageHandler(_ => Json(Named("COELBA")));

        var entitlements = await Sut(handler).GetAsync();

        entitlements.OrganizationName(Coelba).Should().Be("COELBA");
        handler.Requests.Should().ContainSingle().Which.RequestUri!.PathAndQuery.Should().Be("/api/identity/me/cross-org-entitlements");
    }

    [Fact]
    public async Task GetAsync_IsFetchedOnce_UntilRefreshAsync_WhichKeepsTheNewAnswer()
    {
        var name = "COELBA";
        var handler = new FakeHttpMessageHandler(_ => Json(Named(name)));
        var sut = Sut(handler);

        await sut.GetAsync();
        await sut.GetAsync();
        handler.Requests.Should().ContainSingle();

        name = "COELBA (renamed)";
        var refreshed = await sut.RefreshAsync();

        refreshed.OrganizationName(Coelba).Should().Be("COELBA (renamed)");
        (await sut.GetAsync()).Should().BeSameAs(refreshed);
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetAsync_AServerThatDoesNotAnswer_YieldsNone(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(status));

        (await Sut(handler).GetAsync()).Should().BeSameAs(CrossOrgEntitlementsDto.None);
    }

    [Fact]
    public async Task NoCrossOrgEntitlementsService_AnswersNone()
    {
        var sut = new NoCrossOrgEntitlementsService();

        (await sut.GetAsync()).Should().BeSameAs(CrossOrgEntitlementsDto.None);
        (await sut.RefreshAsync()).Should().BeSameAs(CrossOrgEntitlementsDto.None);
    }
}
