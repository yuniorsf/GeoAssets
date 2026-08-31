using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>Cosmetic section label preceding Layers/Assets/Service Orders/Collections (XD01-85).</summary>
public sealed class ManagementSectionItem : MenuSectionItem
{
    public override string Id => "management";
    public override string LabelKey => "nav.management";
    public override int SortOrder => 10;
}
