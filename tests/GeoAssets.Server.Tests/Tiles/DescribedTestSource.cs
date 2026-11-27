using System.Text;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// A deployment-wide keyed tile source for the tests (XD01-459), as a plugin's PMTiles archive or COG would be: vector and
/// raster, answering <c>{id}|{z}/{x}/{y}|{layers}</c>, describing itself as <see cref="Description"/> (or failing, with
/// <see cref="Broken"/>), counting its draws.
/// </summary>
internal sealed class DescribedTestSource(string id, TileSourceDescription description) : IVectorTileSource, IRasterTileSource, IDescribedTileSource
{
    private int _calls;

    public string Id { get; } = id;

    public TileSourceDescription Description { get; set; } = description;

    public bool Broken { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public List<TileRequest> Requests { get; } = [];

    public HashSet<TileAddress> EmptyAt { get; } = [];

    public Task<TileSourceDescription> DescribeAsync(CancellationToken ct) =>
        Broken ? throw new InvalidDataException($"'{Id}' is broken.") : Task.FromResult(Description);

    public Task<TilePayload> GetVectorTileAsync(TileRequest request, CancellationToken ct) => Draw(request, TileFormats.MapboxVectorTileContentType);

    public Task<TilePayload> GetRasterTileAsync(TileRequest request, CancellationToken ct) => Draw(request, TileFormats.PngContentType);

    public static byte[] Content(string id, TileRequest request) =>
        Encoding.UTF8.GetBytes($"{id}|{request.Address}|{string.Join(',', request.LayerSet.LayerIds)}|{new string('k', 500)}");

    private Task<TilePayload> Draw(TileRequest request, string contentType)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests) Requests.Add(request);
        return Task.FromResult(EmptyAt.Contains(request.Address)
            ? TilePayload.Empty(contentType)
            : new TilePayload(Content(Id, request), contentType));
    }
}
