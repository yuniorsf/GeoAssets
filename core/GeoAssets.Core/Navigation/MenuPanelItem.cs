namespace GeoAssets.Core.Navigation;

/// <summary>
/// A leaf item that opens an in-app panel/component rather than navigating to a route. The
/// component is identified by <see cref="Type"/>, not a <c>RenderFragment</c>, so that
/// <c>GeoAssets.Core</c> stays free of a Blazor dependency — the Shared layer resolves and
/// renders the type.
/// </summary>
public abstract class MenuPanelItem : MenuLeafItem
{
    /// <summary>The component type to render for this item.</summary>
    public abstract Type ComponentType { get; }
}
