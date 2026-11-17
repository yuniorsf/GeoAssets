namespace GeoAssets.Shared.Services;

/// <summary>Outcome of <see cref="SelectionFenceOutline.Read"/>. <see cref="Rings"/> is empty
/// unless <see cref="Kind"/> is <see cref="SelectionFenceOutlineKind.Drawable"/>;
/// <see cref="Problem"/> is set only for <see cref="SelectionFenceOutlineKind.Malformed"/>.</summary>
public sealed record SelectionFenceOutlineResult(SelectionFenceOutlineKind Kind, double[][][] Rings, string? Problem);
