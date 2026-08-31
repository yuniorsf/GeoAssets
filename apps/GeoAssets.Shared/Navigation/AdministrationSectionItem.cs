using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>
/// Cosmetic section label preceding Identidad (XD01-85). Carries the same
/// <see cref="RequiredPermission"/> as <see cref="IdentityGroupMenuItem"/> — a section label is
/// never a parent (<see cref="GeoAssets.Core.Navigation.MenuSectionItem"/>'s own doc comment), so
/// tree-pruning a hidden Identidad group wouldn't hide this sibling label on its own. Today's
/// behavior shows/hides both together (a single `ShowAdmin` boolean gated both), so this item
/// duplicates the same permission check to preserve that.
/// </summary>
public sealed class AdministrationSectionItem : MenuSectionItem
{
    public override string Id => "administration";
    public override string LabelKey => "nav.administration";
    public override int SortOrder => 60;
    public override string? RequiredPermission => "users:read";
}
