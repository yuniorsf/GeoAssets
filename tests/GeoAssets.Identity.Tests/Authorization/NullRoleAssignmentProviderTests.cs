using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.Identity.Tests.Authorization;

public class NullRoleAssignmentProviderTests
{
    private readonly NullRoleAssignmentProvider _sut = new();

    [Fact]
    public Task RegisterRoleAsync_DoesNotThrow()
    {
        var role = new AppRole { Name = "Supervisor" };

        var act = () => _sut.RegisterRoleAsync(role);

        return act.Should().NotThrowAsync();
    }

    [Fact]
    public Task UnregisterRoleAsync_DoesNotThrow()
    {
        var act = () => _sut.UnregisterRoleAsync(Guid.NewGuid());

        return act.Should().NotThrowAsync();
    }

    [Fact]
    public Task AssignRoleAsync_DoesNotThrow()
    {
        var act = () => _sut.AssignRoleAsync("user-1", "Supervisor");

        return act.Should().NotThrowAsync();
    }

    [Fact]
    public Task RevokeRoleAsync_DoesNotThrow()
    {
        var act = () => _sut.RevokeRoleAsync("user-1", "Supervisor");

        return act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAssignedRoleNamesAsync_ReturnsEmptyList()
    {
        var result = await _sut.GetAssignedRoleNamesAsync("user-1");

        result.Should().BeEmpty();
    }
}
