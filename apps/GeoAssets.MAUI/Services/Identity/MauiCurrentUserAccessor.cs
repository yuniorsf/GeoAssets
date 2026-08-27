using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Components.Authorization;

namespace GeoAssets.MAUI.Services.Identity;

/// <summary>
/// <see cref="ICurrentUserAccessor"/> for GeoAssets.MAUI (XD01-52) — same shape as
/// <c>BlazorWasmCurrentUserAccessor</c>: reads the current user from
/// <see cref="AuthenticationStateProvider"/> (here, <see cref="MauiMsalAuthenticationStateProvider"/>)
/// via <see cref="ClaimMapping"/> rather than hardcoding Entra's claim shape.
/// </summary>
public sealed class MauiCurrentUserAccessor(
    AuthenticationStateProvider authStateProvider, ClaimMapping? claimMapping = null) : ICurrentUserAccessor
{
    private readonly ClaimMapping _claimMapping = claimMapping ?? ClaimMapping.EntraDefault;

    private CurrentUser? _cached;

    /// <summary>Returns the last authenticated user resolved by <see cref="GetCurrentUserAsync"/>.</summary>
    public CurrentUser? GetCurrentUser() => _cached;

    /// <summary>Resolves the current user from the live MSAL authentication state.</summary>
    public async Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var state = await authStateProvider.GetAuthenticationStateAsync();
        _cached = _claimMapping.Map(state.User);
        return _cached;
    }
}
