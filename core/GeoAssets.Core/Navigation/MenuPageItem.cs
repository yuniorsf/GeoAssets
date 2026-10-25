namespace GeoAssets.Core.Navigation;

/// <summary>A leaf item that navigates to a page route.</summary>
public abstract class MenuPageItem : MenuLeafItem
{
    /// <summary>The route this item links to.</summary>
    public abstract string RouteHref { get; }

    /// <summary>How <see cref="RouteHref"/> is matched against the current URL for "active" styling.</summary>
    public virtual MenuLinkMatch Match => MenuLinkMatch.Prefix;
}
