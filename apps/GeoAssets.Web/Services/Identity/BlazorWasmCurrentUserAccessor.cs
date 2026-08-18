using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Components.Authorization;

namespace GeoAssets.Web.Services.Identity;

/// <summary>
/// Blazor WebAssembly implementation of <see cref="ICurrentUserAccessor"/>.
///
/// Reads the current user from <see cref="AuthenticationStateProvider"/>, which is backed by
/// whatever <see cref="IGeoAuthenticationProvider"/> the host registered (MSAL/Entra by
/// default). Claim-type mapping is delegated to <see cref="ClaimMapping"/> (XD01-48) rather
/// than hardcoded here, so a different IdP's claim shape is a configuration change, not a
/// code change.
///
/// <see cref="GetCurrentUser"/> returns the last cached value (safe for sync callers).
/// <see cref="GetCurrentUserAsync"/> always refreshes from the live auth state.
/// </summary>
public sealed class BlazorWasmCurrentUserAccessor(
    AuthenticationStateProvider authStateProvider, ClaimMapping? claimMapping = null) : ICurrentUserAccessor
{
    private readonly ClaimMapping _claimMapping = claimMapping ?? ClaimMapping.EntraDefault;

    private CurrentUser? _cached;

    /// <summary>Returns the last authenticated user resolved by <see cref="GetCurrentUserAsync"/>.</summary>
    public CurrentUser? GetCurrentUser() => _cached;

    /// <summary>Resolves the current user from the live Blazor authentication state.</summary>
    public async Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var state = await authStateProvider.GetAuthenticationStateAsync();
        _cached = _claimMapping.Map(state.User);
        return _cached;
    }
}
