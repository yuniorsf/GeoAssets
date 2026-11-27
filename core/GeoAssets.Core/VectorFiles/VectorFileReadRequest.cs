namespace GeoAssets.Core.VectorFiles;

/// <summary>
/// One page of a vector file to read (XD01-437): at most <see cref="Limit"/> features of one layer, from the
/// <see cref="Offset"/>-th on — never the whole file at once.
/// </summary>
public sealed record VectorFileReadRequest
{
    /// <summary>The most features one page may hold.</summary>
    public const int MaxLimit = 10_000;

    /// <summary>The page size when none is given.</summary>
    public const int DefaultLimit = 1_000;

    /// <summary>The page of <paramref name="path"/> (a <paramref name="format"/> file) starting at <paramref name="offset"/>, at most <paramref name="limit"/> features.</summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is <see cref="VectorFileFormat.None"/> or not defined, <paramref name="offset"/> is negative, or <paramref name="limit"/> is
    /// outside [1, <see cref="MaxLimit"/>].
    /// </exception>
    public VectorFileReadRequest(string path, VectorFileFormat format, long offset = 0, int limit = DefaultLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (format == VectorFileFormat.None || !Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown vector file format.");
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaxLimit);

        Path = path;
        Format = format;
        Offset = offset;
        Limit = limit;
    }

    /// <summary>The file to read.</summary>
    public string Path { get; }

    /// <summary>Its format.</summary>
    public VectorFileFormat Format { get; }

    /// <summary>How many features of the layer to skip.</summary>
    public long Offset { get; }

    /// <summary>The most features to return.</summary>
    public int Limit { get; }

    /// <summary>The layer to read; <see langword="null"/> = the file's first (FlatGeobuf and GeoParquet files hold one).</summary>
    public string? LayerName { get; init; }
}
