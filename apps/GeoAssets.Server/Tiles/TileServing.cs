using System.Collections.Concurrent;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The tile endpoints' process-wide state (XD01-434): the settings, the level-of-detail policy, the server tile cache, the
/// layer versions, and the raster metatiles being drawn (so the tiles of one metatile, which a map requests together,
/// cost one drawing). Singleton.
/// </summary>
public sealed class TileServing
{
    private readonly ConcurrentDictionary<string, Task<TileDrawing>> _drawing = new(StringComparer.Ordinal);

    public TileServing(TileServingOptions options, ILodPolicy lod, ITileCache cache, TileVersions versions, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(lod);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Options = options;
        Lod = lod;
        Cache = cache;
        Versions = versions;
        TimeProvider = timeProvider;
    }

    public TileServingOptions Options { get; }

    public ILodPolicy Lod { get; }

    public ITileCache Cache { get; }

    public TileVersions Versions { get; }

    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// The drawing of the metatile <paramref name="key"/> names: the one already under way when there is one (shared by
    /// every request that asks meanwhile), else <paramref name="draw"/>'s. A waiter whose shared drawing was cancelled —
    /// its requester went away — while it was not draws for itself.
    /// </summary>
    public async Task<TileDrawing> DrawOnceAsync(
        string key, Func<Task<TileDrawing>> draw, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(draw);

        var mine = new TaskCompletionSource<TileDrawing>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = _drawing.GetOrAdd(key, mine.Task);
        if (shared != mine.Task)
        {
            try
            {
                return await shared.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return await draw().ConfigureAwait(false);
            }
        }

        try
        {
            var drawing = await draw().ConfigureAwait(false);
            mine.SetResult(drawing);
            return drawing;
        }
        catch (OperationCanceledException)
        {
            mine.SetCanceled(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            mine.SetException(ex);
            _ = mine.Task.Exception;
            throw;
        }
        finally
        {
            _drawing.TryRemove(new KeyValuePair<string, Task<TileDrawing>>(key, mine.Task));
        }
    }
}
