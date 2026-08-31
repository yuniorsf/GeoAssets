namespace GeoAssets.Core.Navigation;

/// <summary>
/// One node of the assembled menu tree (built by the discovery mechanism in XD01-81) — pairs a
/// <see cref="MenuItemBase"/> with its already-resolved children.
/// </summary>
public sealed record MenuNode(MenuItemBase Item, IReadOnlyList<MenuNode> Children);
