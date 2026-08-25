using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

public class RestPendingInvitationRepositoryTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static RestPendingInvitationRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse<T>(T body) =>
        new(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(body, options: _opts) };

    private static PendingInvitationDto NewDto(
        string email = "invitee@example.com", string externalObjectId = "invitee-oid",
        InvitationStatus status = InvitationStatus.Pending) =>
        new(Guid.NewGuid(), email, externalObjectId, Guid.NewGuid(), DateTime.UtcNow, null, status);

    [Fact]
    public async Task GetAllPendingAsync_MapsFieldsAndCallsCorrectUrl()
    {
        var dto = NewDto();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<PendingInvitationDto> { dto }));
        var sut = Sut(handler);

        var invitations = await sut.GetAllPendingAsync();

        var invitation = invitations.Should().ContainSingle().Subject;
        invitation.Id.Should().Be(dto.Id);
        invitation.Email.Should().Be(dto.Email);
        invitation.ExternalObjectId.Should().Be(dto.ExternalObjectId);
        invitation.InvitedByUserId.Should().Be(dto.InvitedByUserId);
        invitation.Status.Should().Be(InvitationStatus.Pending);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/invitations");
    }

    [Fact]
    public async Task GetByIdAsync_MatchingInvitationInList_ReturnsIt()
    {
        var dto = NewDto();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<PendingInvitationDto> { dto }));
        var sut = Sut(handler);

        var invitation = await sut.GetByIdAsync(dto.Id);

        invitation.Should().NotBeNull();
        invitation!.Id.Should().Be(dto.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NoMatchInList_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<PendingInvitationDto>()));
        var sut = Sut(handler);

        var invitation = await sut.GetByIdAsync(Guid.NewGuid());

        invitation.Should().BeNull();
    }

    [Fact]
    public async Task GetByExternalObjectIdAsync_MatchingPendingInvitation_ReturnsIt()
    {
        var dto = NewDto(externalObjectId: "target-oid");
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<PendingInvitationDto> { dto }));
        var sut = Sut(handler);

        var invitation = await sut.GetByExternalObjectIdAsync("target-oid");

        invitation.Should().NotBeNull();
        invitation!.ExternalObjectId.Should().Be("target-oid");
    }

    [Fact]
    public async Task GetByExternalObjectIdAsync_AlreadyRedeemed_ReturnsNull()
    {
        // The redirect gate (XD01-71) must stop redirecting once an invitation is redeemed —
        // since GET /invitations only ever lists Pending rows, a redeemed one simply isn't in
        // the list anymore, which is exactly the "stop firing" signal the gate needs.
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<PendingInvitationDto>()));
        var sut = Sut(handler);

        var invitation = await sut.GetByExternalObjectIdAsync("already-redeemed-oid");

        invitation.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_MakesNoHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called."));
        var sut = Sut(handler);

        await sut.SaveChangesAsync();

        handler.Requests.Should().BeEmpty();
    }

    public static IEnumerable<object[]> UnsupportedMethods()
    {
        yield return [Call((IPendingInvitationRepository r) => r.AddAsync(new PendingInvitation()))];
        yield return [Call((IPendingInvitationRepository r) => r.UpdateAsync(new PendingInvitation()))];

        static Func<IPendingInvitationRepository, Task> Call(Func<IPendingInvitationRepository, Task> f) => f;
    }

    [Theory]
    [MemberData(nameof(UnsupportedMethods))]
    public async Task Method_WithNoServerEndpoint_ThrowsNotSupportedException(Func<IPendingInvitationRepository, Task> call)
    {
        var sut = Sut(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called.")));

        var act = () => call(sut);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
