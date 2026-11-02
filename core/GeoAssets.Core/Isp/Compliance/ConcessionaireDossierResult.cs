namespace GeoAssets.Core.Isp.Compliance;

/// <summary>Result of <see cref="IConcessionaireDossierExporter.Export"/> (XD01-284).</summary>
public sealed record ConcessionaireDossierResult(IReadOnlyList<ConcessionaireDossierFile> Files);
