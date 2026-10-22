namespace GeoAssets.Provider.Xlsx;

/// <summary>
/// Generic tabular-row reader over an Excel workbook stream. Scaffolded for XD01-176
/// (Phase 0 of the GEOS_FAT_IP epic) — this epic's three field workflows
/// (Levantamento/Fiscalização/Ofício) each upload a spreadsheet with its own column layout;
/// the per-workflow parsers that map <see cref="ReadRows"/>'s raw rows onto domain records
/// land with their respective calculation-engine tickets (Phases 1-3), not here.
/// </summary>
public interface IWorkbookReader
{
    /// <summary>
    /// Reads every row of the given worksheet as raw cell text, including the header row
    /// (callers decide whether/how to skip it). A row's shorter-than-widest-row cells are
    /// padded with <c>null</c>, never silently dropped.
    /// </summary>
    /// <param name="workbookStream">An open, readable stream positioned at the start of the .xlsx file.</param>
    /// <param name="worksheetName">The worksheet to read, or null for the workbook's first worksheet.</param>
    IReadOnlyList<IReadOnlyList<string?>> ReadRows(Stream workbookStream, string? worksheetName = null);
}
