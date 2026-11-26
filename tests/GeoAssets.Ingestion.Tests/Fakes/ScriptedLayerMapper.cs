using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal sealed class ScriptedLayerMapper : ILayerMapper
{
    private readonly Dictionary<(string Region, string Layer, long Record), Func<string, MappingResult>> _scripts = [];

    public void Script(string region, string layer, long record, Func<string, MappingResult> map) =>
        _scripts[(region, layer, record)] = map;

    public MappingResult Map(RawRecord record, string scope)
    {
        ArgumentNullException.ThrowIfNull(record);
        return _scripts.TryGetValue((record.Layer.Region, record.Layer.Layer, record.RecordNumber), out var map)
            ? map(scope)
            : MappingResult.Skipped(new ImportIssue(IssueSeverity.Error, ImportIssueCodes.UnmappedRecord, $"{record.Layer.Layer}#{record.RecordNumber}", "no script"));
    }
}
