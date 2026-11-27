using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Server.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>A metatile raster source answering a 2 × 2 block of small fake PNGs (one empty), counting its drawings.</summary>
internal sealed class RecordingRasterSource : IRasterTileSource, IRasterMetatileSource
{
    private int _metatiles;

    public int Metatiles => Volatile.Read(ref _metatiles);

    public int MetatileSpan => 2;

    /// <summary>Holds every drawing until released (for concurrent requests).</summary>
    public TaskCompletionSource? Gate { get; set; }

    public TileAddress? EmptyAt { get; set; }

    public Task<TilePayload> GetRasterTileAsync(TileRequest request, CancellationToken ct) => throw new NotSupportedException("the endpoint draws metatiles");

    public async Task<IReadOnlyDictionary<TileAddress, TilePayload>> GetMetatileAsync(TileRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _metatiles);
        if (Gate is { } gate) await gate.Task.WaitAsync(ct);
        return request.Address.Metatile(MetatileSpan).EnumerateTiles().ToDictionary(
            tile => tile,
            tile => tile == EmptyAt
                ? TilePayload.Empty(TileFormats.PngContentType)
                : new TilePayload(Encoding.UTF8.GetBytes($"png|{tile}"), TileFormats.PngContentType));
    }
}
