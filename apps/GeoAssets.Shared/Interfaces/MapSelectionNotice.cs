namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// A message for the selection toolbar, kept as data so the component localizes it (XD01-310).
/// </summary>
public sealed record MapSelectionNotice(MapSelectionNoticeKind Kind, int Count = 0, int Limit = 0)
{
    /// <summary>Whether this reports a failed gesture, as opposed to a partially applied one.</summary>
    public bool IsError => Kind != MapSelectionNoticeKind.Overflow;
}
