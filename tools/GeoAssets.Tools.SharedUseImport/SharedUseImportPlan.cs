namespace GeoAssets.Tools.SharedUseImport;

/// <summary>The outcome of planning an import: every pole to hold on file and every row left out.</summary>
/// <param name="OwnerOrganizationId">The pole owner the import runs as.</param>
/// <param name="RowsRead">How many rows the input had.</param>
/// <param name="Poles">The poles, in ordinal order of pole feature id.</param>
/// <param name="SkippedRows">The rows left out, in input order.</param>
internal sealed record SharedUseImportPlan(
    Guid OwnerOrganizationId,
    int RowsRead,
    IReadOnlyList<SharedUsePolePlan> Poles,
    IReadOnlyList<SkippedSharedUseRow> SkippedRows);
