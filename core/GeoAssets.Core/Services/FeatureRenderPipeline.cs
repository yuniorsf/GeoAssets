using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GeoAssets.Core.Diagnostics;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;

namespace GeoAssets.Core.Services;

/// <summary>
/// Dispatches a provider's full-collection read off the synchronous render call stack, streaming
/// it back in bounded chunks via a <see cref="Channel{T}"/> fed from a background <see cref="Task.Run(Func{Task})"/>.
///
/// Every provider goes through the same <see cref="Task.Run(Func{Task})"/>-fed-channel code path
/// regardless of <see cref="ISyncProvider"/> classification — the marker (checked via
/// <see cref="AssetProviderExtensions.UnwrapToConcrete"/>) only tags the span for telemetry, kept
/// general at the provider level instead of special-casing individual providers at call sites
/// (XD01-160). Callers must keep their own <c>is IWmsProvider</c> short-circuit ahead of any call
/// here — WMS providers render as raster tiles, not through <see cref="IAssetProvider.GetAll"/>.
/// </summary>
public sealed class FeatureRenderPipeline
{
    private const int ChannelCapacity = 4;

    /// <summary>
    /// Streams <paramref name="provider"/>'s full collection in chunks of at most
    /// <paramref name="chunkSize"/> features, yielding each chunk as soon as it's produced rather
    /// than waiting for the whole collection to be ready.
    /// </summary>
    public IAsyncEnumerable<IReadOnlyList<GeoFeature>> StreamAllAsync(
        IAssetProvider provider, int chunkSize, CancellationToken ct = default)
    {
        // Validated eagerly, at call time — an async-iterator method's own body only runs once
        // enumeration starts, so validation inside it would silently defer until the first
        // MoveNextAsync() instead of throwing immediately on a bad call.
        ArgumentNullException.ThrowIfNull(provider);
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize), "chunkSize must be positive.");

        return StreamAllAsyncCore(provider, chunkSize, ct);
    }

    private async IAsyncEnumerable<IReadOnlyList<GeoFeature>> StreamAllAsyncCore(
        IAssetProvider provider, int chunkSize, [EnumeratorCancellation] CancellationToken ct)
    {
        var isSyncProvider = provider.UnwrapToConcrete() is ISyncProvider;
        using var span = ImportDiagnostics.ActivitySource.StartActivity("pipeline.stream_all");
        span?.SetTag("provider.sync_classified", isSyncProvider)
             .SetTag("chunk.size", chunkSize);

        var channel = Channel.CreateBounded<IReadOnlyList<GeoFeature>>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
        });

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var chunk in provider.GetAll().Chunk(chunkSize))
                {
                    ct.ThrowIfCancellationRequested();
                    await channel.Writer.WriteAsync(chunk, ct);
                }
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, ct);

        await foreach (var chunk in channel.Reader.ReadAllAsync(ct))
            yield return chunk;
    }
}
