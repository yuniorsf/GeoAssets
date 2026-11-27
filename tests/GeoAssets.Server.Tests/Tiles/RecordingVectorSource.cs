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

/// <summary>A vector source answering <c>mvt|{org}|{z}/{x}/{y}|{layers}</c> (empty where <see cref="EmptyAt"/> says), counting its calls.</summary>
internal sealed class RecordingVectorSource(IHttpContextAccessor accessor) : IVectorTileSource
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public ConcurrentBag<TileRequest> Requests { get; } = [];

    public HashSet<TileAddress> EmptyAt { get; } = [];

    /// <summary>Runs inside every draw, before it answers (e.g. a write committing meanwhile).</summary>
    public Func<Task>? During { get; set; }

    public async Task<TilePayload> GetVectorTileAsync(TileRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        Requests.Add(request);
        if (During is { } during) await during();
        if (EmptyAt.Contains(request.Address)) return TilePayload.Empty(TileFormats.MapboxVectorTileContentType);

        var organization = accessor.HttpContext?.User.FindFirst("test_org")?.Value is { } org ? Guid.Parse(org) : Guid.Empty;
        return new TilePayload(Content(organization, request), TileFormats.MapboxVectorTileContentType);
    }

    public static byte[] Content(Guid organization, TileRequest request) =>
        Encoding.UTF8.GetBytes($"mvt|{organization}|{request.Address}|{string.Join(',', request.LayerSet.LayerIds)}|{new string('x', 2000)}");
}
