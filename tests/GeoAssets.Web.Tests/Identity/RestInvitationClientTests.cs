using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

public class RestInvitationClientTests
{
    private static RestInvitationClient Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static PendingInvitationDto NewDto(InvitationStatus status = InvitationStatus.Pending) =>
        new(Guid.NewGuid(), "invitee@example.com", "invitee-oid", Guid.NewGuid(), DateTime.UtcNow, null, status);

    [Fact]
    public async Task CreateInvitationAsync_PostsCorrectDtoAndMapsResponse()
    {
        var dto = NewDto();
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(dto) });
        var sut = Sut(handler);

        var invitation = await sut.CreateInvitationAsync("invitee@example.com", "Invitee Name");

        invitation.Id.Should().Be(dto.Id);
        invitation.Email.Should().Be(dto.Email);
        invitation.Status.Should().Be(InvitationStatus.Pending);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/invitations");
        var sentDto = await request.Content!.ReadFromJsonAsync<InvitationCreateDto>();
        sentDto.Should().Be(new InvitationCreateDto("invitee@example.com", "Invitee Name"));
    }

    [Fact]
    public async Task CreateInvitationAsync_PartialFailureStatus202_StillMapsResponse()
    {
        // The server returns 202 (not 201) when the account/row were created but the invite
        // email failed to send (XD01-69) — this must still be treated as success here, not
        // thrown as an error, since EnsureSuccessStatusCode() accepts any 2xx.
        var dto = NewDto();
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(dto) });
        var sut = Sut(handler);

        var invitation = await sut.CreateInvitationAsync("invitee@example.com", "Invitee Name");

        invitation.Id.Should().Be(dto.Id);
    }

    [Fact]
    public async Task RevokeInvitationAsync_SendsDeleteToCorrectUrl()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.RevokeInvitationAsync(id);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be($"/invitations/{id}");
    }

    [Fact]
    public async Task RedeemInvitationAsync_SendsPostToCorrectUrl()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.RedeemInvitationAsync(id);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be($"/invitations/{id}/redeem");
    }
}
