using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.Identity.Tests.Authorization;

public class NullUserInvitationProviderTests
{
    private readonly NullUserInvitationProvider _sut = new();

    [Fact]
    public Task CreateInvitedAccountAsync_DoesNotThrow()
    {
        var act = () => _sut.CreateInvitedAccountAsync("invitee@example.com", "Invitee");

        return act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateInvitedAccountAsync_ReturnsEmptyExternalObjectId()
    {
        var result = await _sut.CreateInvitedAccountAsync("invitee@example.com", "Invitee");

        result.Should().BeEmpty();
    }

    [Fact]
    public Task RevokeInvitedAccountAsync_DoesNotThrow()
    {
        var act = () => _sut.RevokeInvitedAccountAsync("external-oid-1");

        return act.Should().NotThrowAsync();
    }
}
