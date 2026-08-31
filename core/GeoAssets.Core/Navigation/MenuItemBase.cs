namespace GeoAssets.Core.Navigation;

/// <summary>
/// Base type for every left-nav-menu item (XD01-79). Concrete items are discovered by
/// reflection and assembled into a tree via <see cref="ParentId"/> matching (XD01-81) — a
/// discovered item can't hold a compile-time reference to a parent instance it doesn't own.
/// </summary>
public abstract class MenuItemBase
{
    /// <summary>Stable identifier, unique across the whole menu tree.</summary>
    public abstract string Id { get; }

    /// <summary>i18n key resolved via <c>@L["key"]</c> (see GeoAssets.Shared's Localization components).</summary>
    public abstract string LabelKey { get; }

    /// <summary>Icon identifier for the item, if any — rendering convention is decided by the Shared layer.</summary>
    public virtual string? Icon => null;

    /// <summary>Ordering among sibling items — lower sorts first.</summary>
    public virtual int SortOrder => 0;

    /// <summary><see cref="Id"/> of this item's parent, or <c>null</c> for a root-level item.</summary>
    public virtual string? ParentId => null;

    /// <summary>
    /// Permission code required to see this item, in the same <c>"resource:action"</c> code
    /// convention as <c>IGeoAuthorizationService.HasPermissionAsync</c>. Not yet evaluated
    /// anywhere — this field exists so items can declare it now (evaluation lands in XD01-85).
    /// </summary>
    public virtual string? RequiredPermission => null;
}
