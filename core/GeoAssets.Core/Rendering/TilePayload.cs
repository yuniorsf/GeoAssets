using System.Security.Cryptography;
using System.Text;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// The bytes of one served tile (XD01-431), as a tile source answers and an <see cref="ITileCache"/> keeps them:
/// <see cref="Content"/> in <see cref="ContentType"/>, already compressed with <see cref="ContentEncoding"/> when set
/// (e.g. the gzip a PMTiles archive stores its vector tiles in), so an endpoint passes it through unchanged. An empty
/// tile (nothing there) has no content — an endpoint may answer it with <c>204 No Content</c>. Immutable as long as
/// whoever built it doesn't keep writing to the buffer behind <see cref="Content"/>.
/// </summary>
public sealed class TilePayload
{
    private string? _etag;

    /// <summary>A tile of <paramref name="content"/> in <paramref name="contentType"/>, compressed with <paramref name="contentEncoding"/> (<c>null</c> = not).</summary>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> is blank, or <paramref name="contentEncoding"/> is empty or blank.</exception>
    public TilePayload(ReadOnlyMemory<byte> content, string contentType, string? contentEncoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (contentEncoding is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(contentEncoding);

        Content = content;
        ContentType = contentType;
        ContentEncoding = contentEncoding;
    }

    /// <summary>The tile's bytes, as served.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>The media type, e.g. <see cref="TileFormats.MapboxVectorTileContentType"/>.</summary>
    public string ContentType { get; }

    /// <summary>The HTTP <c>Content-Encoding</c> <see cref="Content"/> is already in (e.g. <c>gzip</c>); <c>null</c> when it is not compressed.</summary>
    public string? ContentEncoding { get; }

    /// <summary>Whether the tile has no content.</summary>
    public bool IsEmpty => Content.IsEmpty;

    /// <summary>
    /// A strong HTTP entity tag (quoted) derived from <see cref="Content"/> and <see cref="ContentEncoding"/> alone:
    /// equal bytes give equal tags, so a client's <c>If-None-Match</c> still matches after the tile is evicted and
    /// rebuilt unchanged, and never matches once it changed. Computed on first read.
    /// </summary>
    public string ETag => _etag ??= ComputeETag(Content.Span, ContentEncoding);

    /// <summary>An empty tile of <paramref name="contentType"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> is blank.</exception>
    public static TilePayload Empty(string contentType) => new(ReadOnlyMemory<byte>.Empty, contentType);

    private static string ComputeETag(ReadOnlySpan<byte> content, string? contentEncoding)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(contentEncoding ?? string.Empty));
        hash.AppendData([0]);
        hash.AppendData(content);
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return "\"" + Convert.ToHexStringLower(digest[..16]) + "\"";
    }
}
