using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

public class RestUserClaimRepositoryTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static RestUserClaimRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: _opts) };

    [Fact]
    public async Task GetByUserIdAsync_MapsFieldsAndCallsCorrectUrl()
    {
        var dto = new UserClaimDto(Guid.NewGuid(), "zone", "north");
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<UserClaimDto> { dto }));
        var sut = Sut(handler);
        var callerId = Guid.NewGuid();

        var claims = await sut.GetByUserIdAsync(callerId);

        var claim = claims.Should().ContainSingle().Subject;
        claim.Id.Should().Be(dto.Id);
        claim.Type.Should().Be("zone");
        claim.Value.Should().Be("north");
        // Self-service backend — the server never reports UserId, so the caller-supplied id
        // is the only source of truth for it client-side.
        claim.UserId.Should().Be(callerId);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/userclaims");
    }

    [Fact]
    public async Task GetAsync_Found_ReturnsClaim()
    {
        var dto = new UserClaimDto(Guid.NewGuid(), "department", "operations");
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(dto));
        var sut = Sut(handler);

        var claim = await sut.GetAsync(Guid.NewGuid(), "department");

        claim.Should().NotBeNull();
        claim!.Value.Should().Be("operations");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/userclaims/department");
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var claim = await sut.GetAsync(Guid.NewGuid(), "department");

        claim.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_PostsWriteDtoAndSyncsServerAssignedId()
    {
        // Regression guard: the server always mints its own Id (UserClaimWriteDto carries no Id
        // field) and returns it only in the response body — without re-reading it, claim.Id
        // would stay stuck at whatever the caller happened to set it to beforehand.
        var serverAssignedId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(new UserClaimDto(serverAssignedId, "phone", "+1-555-0100"), HttpStatusCode.Created));
        var sut = Sut(handler);
        var claim = new UserClaim { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Type = "phone", Value = "+1-555-0100" };
        var callerSuppliedId = claim.Id;

        await sut.AddAsync(claim);

        claim.Id.Should().Be(serverAssignedId);
        claim.Id.Should().NotBe(callerSuppliedId);
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/userclaims");
        var sentDto = await request.Content!.ReadFromJsonAsync<UserClaimWriteDto>();
        sentDto.Should().Be(new UserClaimWriteDto("phone", "+1-555-0100"));
    }

    [Fact]
    public async Task UpdateAsync_SendsUpdateDtoToCorrectUrl()
    {
        var claim = new UserClaim { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Type = "zone", Value = "south" };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.UpdateAsync(claim);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.AbsolutePath.Should().Be($"/userclaims/{claim.Id}");
        var sentDto = await request.Content!.ReadFromJsonAsync<UserClaimUpdateDto>();
        sentDto.Should().Be(new UserClaimUpdateDto("south"));
    }

    [Fact]
    public async Task RemoveAsync_SendsDeleteToCorrectUrl()
    {
        var claimId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.RemoveAsync(claimId);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be($"/userclaims/{claimId}");
    }

    [Fact]
    public async Task RemoveAllAsync_SendsDeleteToTheCollectionUrl()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.RemoveAllAsync(Guid.NewGuid());

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be("/userclaims");
    }

    [Fact]
    public async Task SaveChangesAsync_MakesNoHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called."));
        var sut = Sut(handler);

        await sut.SaveChangesAsync();

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByTypeAsync_NoServerEndpointExists_ThrowsNotSupportedException()
    {
        // Cross-user query — "every user with claim type X" — has no self-service mapping and
        // no server endpoint; this is the one method XD01-87 deliberately left unimplemented.
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called."));
        var sut = Sut(handler);

        var act = () => sut.GetByTypeAsync("zone");

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
