using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.SharedUse;

/// <summary>How a target's activities are written in a table cell (XD01-316).</summary>
public static class TargetActivityLabels
{
    /// <summary>What a target with no activity shows.</summary>
    public const string None = "—";

    /// <summary>
    /// The activities' display names from the order type's catalog, in the order the target
    /// lists them, joined by a comma; an id the catalog does not know is shown as it is.
    /// </summary>
    public static string Describe(IReadOnlyList<string>? activityIds, IReadOnlyList<OrderTargetActivity>? catalog)
    {
        if (activityIds is not { Count: > 0 }) return None;

        return string.Join(", ", activityIds.Select(id =>
            catalog?.FirstOrDefault(activity => string.Equals(activity.Id, id, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? id));
    }
}
