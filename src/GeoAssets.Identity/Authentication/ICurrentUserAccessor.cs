namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Abstraction for resolving the currently authenticated user from the ambient context.
///
/// Implementations:
///   • <see cref="ClaimsPrincipalCurrentUserAccessor"/> — ASP.NET Core / Blazor Server
///   • <c>BlazorWasmCurrentUserAccessor</c>             — Blazor WebAssembly (async MSAL)
///   • Test stubs returning any fixture user
///
/// The default <see cref="GetCurrentUserAsync"/> wraps the synchronous method.
/// WASM implementations override it to read from AuthenticationStateProvider.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>Synchronous accessor. May return a cached value in async hosts.</summary>
    CurrentUser? GetCurrentUser();

    /// <summary>
    /// Async accessor — preferred when available.
    /// The default implementation wraps <see cref="GetCurrentUser"/>.
    /// </summary>
    Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default)
        => Task.FromResult(GetCurrentUser());
}
