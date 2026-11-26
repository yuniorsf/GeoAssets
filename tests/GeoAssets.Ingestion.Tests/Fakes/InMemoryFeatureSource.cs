using System.Runtime.CompilerServices;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal sealed class InMemoryFeatureSource : IFeatureSource
{
    private readonly Dictionary<(string Region, string Layer), List<RawRecord>> _records = [];

    public List<(string Region, string Layer)> Reads { get; } = [];

    public Func<RawRecord, Task>? OnRecord { get; set; }

    public void Add(LayerFileSet layer, RawRecord record)
    {
        var key = (layer.Region, layer.Layer);
        if (!_records.TryGetValue(key, out var list))
        {
            list = [];
            _records[key] = list;
        }

        list.Add(record);
    }

    public int RecordsInLayer(string region, string layer) =>
        _records.TryGetValue((region, layer), out var list) ? list.Count : 0;

    public async IAsyncEnumerable<RawRecord> ReadAsync(LayerFileSet layer, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Reads.Add((layer.Region, layer.Layer));
        await Task.Yield();
        if (!_records.TryGetValue((layer.Region, layer.Layer), out var list))
        {
            yield break;
        }

        foreach (var record in list.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OnRecord is not null)
            {
                await OnRecord(record);
            }

            yield return record;
        }
    }
}
