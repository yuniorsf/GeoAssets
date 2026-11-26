using GeoAssets.Ingestion.Spatial;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal sealed class IdentityCoordinateTransformerFactory : ICoordinateTransformerFactory
{
    private readonly Dictionary<string, string> _accuracyByWkt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unsupported = new(StringComparer.Ordinal);

    public List<string> Created { get; } = [];

    public string DefaultAccuracy { get; set; } = "none";

    public IdentityCoordinateTransformerFactory WithAccuracy(string crsWkt, string accuracy)
    {
        _accuracyByWkt[crsWkt] = accuracy;
        return this;
    }

    public IdentityCoordinateTransformerFactory Rejecting(string crsWkt)
    {
        _unsupported.Add(crsWkt);
        return this;
    }

    public ICoordinateTransformer Create(string sourceCrsWkt)
    {
        Created.Add(sourceCrsWkt);
        if (_unsupported.Contains(sourceCrsWkt))
        {
            throw new NotSupportedException($"CRS '{sourceCrsWkt}' is not supported.");
        }

        return new IdentityCoordinateTransformer(_accuracyByWkt.GetValueOrDefault(sourceCrsWkt, DefaultAccuracy));
    }
}
