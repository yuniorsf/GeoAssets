using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.Identity.Tests.Authorization;

public class NullInvitationEmailSenderTests
{
    private readonly NullInvitationEmailSender _sut = new();

    [Fact]
    public async Task SendInvitationAsync_DoesNotThrow()
    {
        var act = () => _sut.SendInvitationAsync("invitee@example.com", "Invitee");

        await act.Should().NotThrowAsync();
    }
}
