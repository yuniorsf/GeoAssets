using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Shared.Tests.Services.ReferenceLayers;

/// <summary>The caller's home context (<c>/me</c>); counts its reads.</summary>
internal sealed class FakeAuthorization(AuthorizationContext home) : IGeoAuthorizationService
{
    public int Reads { get; private set; }

    public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult(home);
    }
}
