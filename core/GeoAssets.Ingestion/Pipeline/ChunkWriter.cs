using GeoAssets.Core.Models;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>
/// Buffers projected features and writes them to the sink in numbered chunks of at most the chunk size, flushing
/// whenever the scope changes so every chunk holds one scope. Sink rejections are added to the issues.
/// </summary>
internal sealed class ChunkWriter(IFeatureSink sink, int chunkSize, IssueLog issues)
{
    private readonly int _chunkSize = Math.Max(1, chunkSize);
    private List<GeoFeature> _buffer = [];
    private string? _scope;

    public int ChunksWritten { get; private set; }

    public long FeaturesWritten { get; private set; }

    public async Task AddAsync(GeoFeature feature, string scope, CancellationToken cancellationToken)
    {
        if (_buffer.Count > 0 && !string.Equals(_scope, scope, StringComparison.Ordinal))
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        _scope = scope;
        _buffer.Add(feature);
        if (_buffer.Count >= _chunkSize)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_buffer.Count == 0)
        {
            return;
        }

        var chunk = _buffer;
        _buffer = [];
        var result = await sink.WriteChunkAsync(ChunksWritten, chunk, cancellationToken).ConfigureAwait(false);
        ChunksWritten++;
        FeaturesWritten += result.Written;
        issues.AddRange(result.Rejections);
    }
}
