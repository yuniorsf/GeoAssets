using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Xunit;
using PageCompleteProfile = GeoAssets.Shared.Pages.CompleteProfile;

namespace GeoAssets.Shared.Tests.Pages;

public class CompleteProfileTests
{
    private sealed class StubGeoAuthorizationService(AuthorizationContext context) : IGeoAuthorizationService
    {
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
        {
            ReceivedCancellationToken = ct;
            return Task.FromResult(context);
        }
    }

    private static AuthorizationContext BuildContext(Guid userId) => new()
    {
        User = new AppUser
        {
            Id        = userId,
            CreatedAt = DateTime.UtcNow,
        },
        Roles       = [],
        Claims      = [],
        Permissions = [],
    };

    // Reproduces the XD01-93 crash scenario: under Identity:Backend=Rest, resolving the
    // caller's own AppUser id must go through IGeoAuthorizationService (backed by GET /me),
    // never IUserRepository.GetByExternalObjectIdAsync — RestUserRepository throws
    // NotSupportedException for that member. This test would fail to compile against the
    // pre-fix signature (which took an IUserRepository and called the unsupported member).
    [Fact]
    public async Task ResolveAppUserIdAsync_ReturnsContextUserId()
    {
        var userId = Guid.NewGuid();
        var authService = new StubGeoAuthorizationService(BuildContext(userId));

        var result = await PageCompleteProfile.ResolveAppUserIdAsync(authService);

        result.Should().Be(userId);
    }

    [Fact]
    public async Task ResolveAppUserIdAsync_PassesCancellationTokenThrough()
    {
        using var cts = new CancellationTokenSource();
        var authService = new StubGeoAuthorizationService(BuildContext(Guid.NewGuid()));

        await PageCompleteProfile.ResolveAppUserIdAsync(authService, cts.Token);

        authService.ReceivedCancellationToken.Should().Be(cts.Token);
    }
}
