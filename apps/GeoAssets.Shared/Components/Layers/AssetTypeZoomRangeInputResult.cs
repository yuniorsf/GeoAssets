namespace GeoAssets.Shared.Components.Layers;

/// <summary>
/// What the editor's two zoom fields mean. On success <see cref="Error"/> is null and
/// <see cref="MinZoom"/>/<see cref="MaxZoom"/> hold the range (null = open bound); on failure
/// both are null and <see cref="MinInvalid"/>/<see cref="MaxInvalid"/> say which field to flag.
/// </summary>
public sealed record AssetTypeZoomRangeInputResult(
    int? MinZoom, int? MaxZoom, AssetTypeZoomRangeInputError? Error, bool MinInvalid, bool MaxInvalid)
{
    public bool IsValid => Error is null;
}
