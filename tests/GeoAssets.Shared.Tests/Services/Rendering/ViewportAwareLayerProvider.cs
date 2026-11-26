using System.Runtime.CompilerServices;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// A <see cref="FakeLayerProvider"/> that overrides the <see cref="ViewportQuery"/> reads (XD01-429), as REST does:
/// it keeps the viewport and token each was asked with, yields <paramref name="chunks"/> as its raw-JSON chunks and
/// answers no JSON elements.
/// </summary>
internal sealed class ViewportAwareLayerProvider(IReadOnlyList<string> chunks) : FakeLayerProvider, IAssetProvider
{
    public ViewportQuery? ChunkViewport { get; private set; }
    public CancellationToken ChunkToken { get; private set; }
    public ViewportQuery? JsonViewport { get; private set; }
    public CancellationToken JsonToken { get; private set; }

    public async IAsyncEnumerable<string> GetInBoundsRawJsonChunksAsync(
        ViewportQuery viewport, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ChunkViewport = viewport;
        ChunkToken = ct;
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(ViewportQuery viewport, CancellationToken ct = default)
    {
        JsonViewport = viewport;
        JsonToken = ct;
        return Task.FromResult<IReadOnlyList<JsonElement>>([]);
    }
}
