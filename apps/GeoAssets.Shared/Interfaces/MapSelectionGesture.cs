namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// A completed area gesture from the map (XD01-310), as drawn — not yet normalised or validated;
/// <c>MapSelectionRequest.FromGesture</c> does that.
/// </summary>
/// <param name="Tool"><see cref="MapSelectionTool.Box"/> or <see cref="MapSelectionTool.Fence"/>.</param>
/// <param name="Rings">Rings of <c>[longitude, latitude]</c> positions (RFC 7946 order). For a box,
/// any positions whose bounding rectangle is the box — two opposite corners are enough. For a
/// fence, the exterior ring first; it need not be closed.</param>
/// <param name="Additive">Whether the additive modifier (Shift) was held: add to the selection
/// instead of replacing it.</param>
public sealed record MapSelectionGesture(MapSelectionTool Tool, double[][][] Rings, bool Additive);
