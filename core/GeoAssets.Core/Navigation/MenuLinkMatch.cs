namespace GeoAssets.Core.Navigation;

/// <summary>
/// Match strategy for <see cref="MenuPageItem.RouteHref"/> against the current URL. Mirrors
/// Blazor's <c>NavLinkMatch</c> without referencing it — <c>GeoAssets.Core</c> has no Blazor
/// dependency — the Shared-layer renderer translates this to the real enum.
/// </summary>
public enum MenuLinkMatch
{
    Prefix,
    Exact,
}
