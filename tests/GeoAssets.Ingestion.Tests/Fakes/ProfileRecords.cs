using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;
using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal static class ProfileRecords
{
    public const string Scope = "coelba/Salvador/POSTE";

    public static RawRecord Record(string layer, Geometry geometry, params (string Name, object? Value)[] attributes)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in attributes)
        {
            values[name] = value;
        }

        var fileSet = new LayerFileSet { Layer = layer, Region = "Salvador", Verdict = LayerVerdict.Usable };
        return new RawRecord(fileSet, 1, geometry, values);
    }

    public static MappingResult Map(ISourceProfile profile, RawRecord record) =>
        profile.MapperFor(record.Layer.Layer).Map(record, Scope);
}
