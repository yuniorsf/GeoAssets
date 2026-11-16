namespace GeoAssets.Core.Interfaces;

/// <summary>Outcome of one <see cref="IBdgdPonnotImportService.ImportAsync"/> run.</summary>
public sealed record BdgdPonnotImportResult(
    int Inserted,
    int Updated,
    int Skipped,
    IReadOnlyList<string> Warnings);
