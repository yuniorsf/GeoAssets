namespace GeoAssets.Tools.SharedUseImport;

/// <summary>Why a legacy row was left out of the import. A skipped row adds no entry to any pole.</summary>
internal enum SharedUseRowSkipReason
{
    /// <summary>An earlier row of the input has the same legacy id; the first one is imported.</summary>
    DuplicateRow,

    /// <summary>The row stands for zero or fewer attachment points.</summary>
    NonPositiveCount,

    /// <summary>The mapping names no pole feature for the row's legacy pole.</summary>
    UnmappedPole,
}
