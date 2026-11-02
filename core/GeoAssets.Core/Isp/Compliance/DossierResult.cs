namespace GeoAssets.Core.Isp.Compliance;

/// <summary>Result of <see cref="IDossierExporter.Export"/> (XD01-284).</summary>
public sealed record DossierResult(IReadOnlyList<DossierFile> Files);
