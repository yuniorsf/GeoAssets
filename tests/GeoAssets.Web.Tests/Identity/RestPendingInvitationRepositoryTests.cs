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
    public async Task GetByExternalObjectIdAsync_OwnPendingInvitationExists_ReturnsIt()
    {
        // XD01-92: calls the self-service GET /invitations/me endpoint, not the admin list —
        // the admin list requires users:read, a permission the redirect gate's only real
        // caller (a just-invited, zero-permissions user) never has.
        var dto = NewDto(externalObjectId: "target-oid");
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(dto));
        var sut = Sut(handler);

        var invitation = await sut.GetByExternalObjectIdAsync("target-oid");

        invitation.Should().NotBeNull();
        invitation!.ExternalObjectId.Should().Be("target-oid");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/invitations/me");
    }

    [Fact]
    public async Task GetByExternalObjectIdAsync_NoOwnPendingInvitation_ReturnsNull()
    {
        // Covers both "never invited" and "already redeemed/revoked" — the server-side
        // /invitations/me endpoint returns 200 with a null body for both, since it only ever
        // resolves a Pending row (never a 404 — that's not an error case for this endpoint).
        // The redirect gate (XD01-71/89) needs exactly this "stop firing" signal once redeemed.
        var handler = new FakeHttpMessageHandler(_ => JsonResponse<PendingInvitationDto?>(null));
        var sut = Sut(handler);

        var invitation = await sut.GetByExternalObjectIdAsync("no-invitation-oid");

        invitation.Should().BeNull();
    }

    [Fact]
    public async Task GetByExternalObjectIdAsync_ParameterIsIgnored_AlwaysChecksTheCallersOwnInvitation()
    {
        // Documented assumption (XD01-92): this method's externalObjectId parameter is never
        // sent to the server — /invitations/me resolves "my own" from the caller's
        // authenticated identity server-side, regardless of what's passed here.
        var dto = NewDto(externalObjectId: "the-actual-caller-oid");
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(dto));
        var sut = Sut(handler);

        var invitation = await sut.GetByExternalObjectIdAsync("some-other-id-entirely");

        invitation.Should().NotBeNull();
        invitation!.ExternalObjectId.Should().Be("the-actual-caller-oid");
        handler.Requests.Single().RequestUri!.Query.Should().BeEmpty();
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
