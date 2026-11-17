using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// What the preview panel shows and allows for one <see cref="TargetSelectionPreview"/>
/// (XD01-312). Counts are the server's; this only derives the decisions from them.
/// </summary>
public sealed record SelectionPreviewSummary
{
    private SelectionPreviewSummary(TargetSelectionPreview preview) => Preview = preview;

    public TargetSelectionPreview Preview { get; }

    /// <summary>How many targets applying would add: the eligible features that fit under the limit.</summary>
    public int ToAdd => Math.Max(Preview.Eligible - Preview.TruncatedByLimit, 0);

    /// <summary>Whether the order type's selection limit leaves eligible features out.</summary>
    public bool IsTruncated => Preview.TruncatedByLimit > 0;

    /// <summary>Whether applying would add nothing (nothing matched, or everything is held or cut).</summary>
    public bool HasNothingToAdd => ToAdd == 0;

    /// <summary>
    /// Requested identifiers the server did not resolve for the order type: of another asset type,
    /// or gone. Identifier-list runs only; a fence reports none.
    /// </summary>
    public int NotResolvedCount => Preview.NotFoundValues.Count;

    /// <summary>
    /// Whether the apply button is enabled: something would be added and, when the limit cuts the
    /// selection, the user explicitly accepted that. Blocked targets do not prevent applying — they
    /// are added flagged, as the server does.
    /// </summary>
    public bool CanApply(bool truncationAccepted) => !HasNothingToAdd && (!IsTruncated || truncationAccepted);

    /// <exception cref="ArgumentNullException"><paramref name="preview"/> is null.</exception>
    public static SelectionPreviewSummary From(TargetSelectionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new SelectionPreviewSummary(preview);
    }
}
