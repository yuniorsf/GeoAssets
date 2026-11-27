using System.IO.Compression;

namespace GeoAssets.Provider.PMTiles;

/// <summary>Decompresses (and compresses) PMTiles sections and tiles with the codecs .NET has: gzip and Brotli (XD01-437).</summary>
internal static class PMTilesCodec
{
    /// <summary>The HTTP <c>Content-Encoding</c> of <paramref name="compression"/>, <see langword="null"/> for none.</summary>
    /// <exception cref="PMTilesFormatException"><paramref name="compression"/> is not a known code.</exception>
    public static string? ContentEncoding(PMTilesCompression compression) => compression switch
    {
        PMTilesCompression.None => null,
        PMTilesCompression.Gzip => "gzip",
        PMTilesCompression.Brotli => "br",
        PMTilesCompression.Zstd => "zstd",
        _ => throw new PMTilesFormatException($"Unknown PMTiles compression code {(byte)compression}."),
    };

    /// <summary>
    /// The compression of one tile stored under <paramref name="declared"/>: the declared one, or — when the archive
    /// says <see cref="PMTilesCompression.Unknown"/> — gzip if the bytes start with its magic, else none.
    /// </summary>
    public static PMTilesCompression Resolve(PMTilesCompression declared, ReadOnlySpan<byte> tile) =>
        declared != PMTilesCompression.Unknown ? declared
        : tile.Length >= 2 && tile[0] == 0x1F && tile[1] == 0x8B ? PMTilesCompression.Gzip
        : PMTilesCompression.None;

    /// <summary>The bytes of <paramref name="data"/> decompressed with <paramref name="compression"/>, at most <paramref name="maxLength"/> of them.</summary>
    /// <exception cref="PMTilesFormatException">The codec isn't available (Zstandard, unknown), the data is corrupt, or it decompresses past <paramref name="maxLength"/>.</exception>
    public static byte[] Decompress(ReadOnlyMemory<byte> data, PMTilesCompression compression, int maxLength)
    {
        if (compression == PMTilesCompression.None)
            return data.ToArray();

        using var input = new MemoryStream(data.ToArray(), writable: false);
        using Stream decoder = compression switch
        {
            PMTilesCompression.Gzip => new GZipStream(input, CompressionMode.Decompress),
            PMTilesCompression.Brotli => new BrotliStream(input, CompressionMode.Decompress),
            _ => throw new PMTilesFormatException($"PMTiles {compression} compression can't be decoded here (gzip, Brotli or none only)."),
        };

        try
        {
            return ReadBounded(decoder, maxLength);
        }
        catch (InvalidDataException ex)
        {
            throw new PMTilesFormatException($"Corrupt {compression}-compressed PMTiles data: {ex.Message}", ex);
        }
    }

    /// <summary>Compresses <paramref name="data"/> with <paramref name="compression"/> (gzip, Brotli or none).</summary>
    /// <exception cref="PMTilesFormatException">The codec isn't available.</exception>
    public static byte[] Compress(ReadOnlySpan<byte> data, PMTilesCompression compression)
    {
        if (compression == PMTilesCompression.None)
            return data.ToArray();

        using var output = new MemoryStream();
        using (Stream encoder = compression switch
        {
            PMTilesCompression.Gzip => new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true),
            PMTilesCompression.Brotli => new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true),
            _ => throw new PMTilesFormatException($"PMTiles {compression} compression can't be encoded here (gzip, Brotli or none only)."),
        })
        {
            encoder.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] ReadBounded(Stream decoder, int maxLength)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = decoder.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > maxLength)
                throw new PMTilesFormatException($"PMTiles data decompresses past the {maxLength}-byte limit.");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
