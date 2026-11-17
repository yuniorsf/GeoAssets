namespace GeoAssets.Tools.SharedUseImport;

/// <summary>The reconciliation report of one import run.</summary>
/// <param name="GeneratedAt">When the run finished (the injected clock).</param>
/// <param name="DryRun">True when nothing was written.</param>
/// <param name="OwnerOrganizationId">The pole owner the import ran as.</param>
/// <param name="Totals">Counts over the whole run.</param>
/// <param name="Poles">Every planned pole, in ordinal order of pole feature id.</param>
/// <param name="SkippedRows">Every row left out, in input order.</param>
internal sealed record SharedUseImportReport(
    DateTimeOffset GeneratedAt,
    bool DryRun,
    Guid OwnerOrganizationId,
    SharedUseImportTotals Totals,
    IReadOnlyList<SharedUsePoleReport> Poles,
    IReadOnlyList<SkippedSharedUseRow> SkippedRows);
