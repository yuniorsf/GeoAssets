using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.Identity.Tests.Authorization;

public class NullUserInvitationProviderTests
{
    private readonly NullUserInvitationProvider _sut = new();

    [Fact]
    public async Task CreateInvitedAccountAsync_DoesNotThrow()
    {
        var act = () => _sut.CreateInvitedAccountAsync("invitee@example.com", "Invitee");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateInvitedAccountAsync_ReturnsEmptyExternalObjectId()
    {
        var result = await _sut.CreateInvitedAccountAsync("invitee@example.com", "Invitee");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeInvitedAccountAsync_DoesNotThrow()
    {
        var act = () => _sut.RevokeInvitedAccountAsync("external-oid-1");

        await act.Should().NotThrowAsync();
    }
}
