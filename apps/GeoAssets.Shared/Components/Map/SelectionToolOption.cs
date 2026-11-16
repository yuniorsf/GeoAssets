using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Components.Map;

/// <summary>One toggle button of <see cref="SelectionToolbar"/>: the tool it arms, its decorative
/// glyph, and the localization keys of its label and tooltip.</summary>
public sealed record SelectionToolOption(MapSelectionTool Tool, string Glyph, string LabelKey, string TitleKey);
