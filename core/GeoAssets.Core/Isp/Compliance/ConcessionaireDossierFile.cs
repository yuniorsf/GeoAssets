namespace GeoAssets.Core.Isp.Compliance;

/// <summary>One named output file within a <see cref="ConcessionaireDossierResult"/> (XD01-284).</summary>
public sealed record ConcessionaireDossierFile(string FileName, byte[] Content);
