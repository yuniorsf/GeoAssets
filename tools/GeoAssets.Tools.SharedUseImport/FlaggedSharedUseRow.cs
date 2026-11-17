namespace GeoAssets.Tools.SharedUseImport;

/// <summary>One flag raised on one imported legacy row; a row may carry several.</summary>
internal sealed record FlaggedSharedUseRow(int LegacyRowId, int LineNumber, SharedUseRowFlag Flag);
