namespace GeoAssets.Core.Isp.Compliance;

/// <summary>One named output file within a <see cref="DossierResult"/> (XD01-284).</summary>
public sealed record DossierFile(string FileName, byte[] Content);
