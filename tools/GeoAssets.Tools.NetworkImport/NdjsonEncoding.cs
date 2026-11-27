using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// Writes features as NDJSON: one compact <see cref="GeoJsonSerializer"/> GeoFeature per line (the same converters
/// as <see cref="GeoJsonSerializer.Options"/>, without indentation so each feature stays on one line).
/// </summary>
internal static class NdjsonEncoding
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    public static async Task WriteAsync(Stream stream, IEnumerable<GeoFeature> features, CancellationToken cancellationToken)
    {
        foreach (var feature in features)
        {
            await JsonSerializer.SerializeAsync(stream, feature, GeoJsonSerializer.CompactOptions, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The features of the NDJSON file <paramref name="path"/>, one line at a time.</summary>
    public static async IAsyncEnumerable<GeoFeature> ReadAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line.Length > 0)
                    yield return JsonSerializer.Deserialize<GeoFeature>(line, GeoJsonSerializer.CompactOptions)!;
            }
        }
    }

    /// <summary>The features as a gzip-compressed NDJSON body.</summary>
    public static async Task<byte[]> GzipAsync(IEnumerable<GeoFeature> features, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true);
        await using (gzip.ConfigureAwait(false))
            await WriteAsync(gzip, features, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
