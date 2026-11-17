namespace GeoAssets.Tools.SharedUseImport;

/// <summary>A legacy row left out of the import, and why.</summary>
internal sealed record SkippedSharedUseRow(int LegacyRowId, int LegacyPoleId, int LineNumber, SharedUseRowSkipReason Reason);
