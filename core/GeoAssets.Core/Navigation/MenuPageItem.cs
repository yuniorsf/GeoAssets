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

/// <summary>A leaf item that navigates to a page route.</summary>
public abstract class MenuPageItem : MenuLeafItem
{
    /// <summary>The route this item links to.</summary>
    public abstract string RouteHref { get; }

    /// <summary>How <see cref="RouteHref"/> is matched against the current URL for "active" styling.</summary>
    public virtual MenuLinkMatch Match => MenuLinkMatch.Prefix;
}
