using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>
/// The group of a group-anchor reference (<see cref="ElementReference.GroupKey"/> set and not blank): its
/// <see cref="ElementReference.TargetNamespace"/> and its trimmed group key.
/// </summary>
internal static class GroupAnchorKey
{
    public static bool IsGroupAnchor(ElementReference reference) => !string.IsNullOrWhiteSpace(reference.GroupKey);

    public static bool TryGet(ElementReference reference, out IndexKey group)
    {
        if (!IsGroupAnchor(reference))
        {
            group = default;
            return false;
        }

        group = new IndexKey(reference.TargetNamespace, reference.GroupKey!.Trim());
        return true;
    }
}
