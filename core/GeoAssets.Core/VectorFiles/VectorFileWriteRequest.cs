namespace GeoAssets.Core.VectorFiles;

/// <summary>
/// How an <see cref="IVectorFileWriter"/> lays out the file it writes (XD01-466): one layer (FlatGeobuf and GeoParquet
/// files hold one, named after the file).
/// </summary>
public sealed record VectorFileWriteRequest
{
    /// <summary>The batch size when none is given.</summary>
    public const int DefaultBatchSize = 1_000;

    private readonly int _batchSize = DefaultBatchSize;

    /// <summary>A <paramref name="format"/> file.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is <see cref="VectorFileFormat.None"/> or not defined.</exception>
    public VectorFileWriteRequest(VectorFileFormat format)
    {
        if (format == VectorFileFormat.None || !Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown vector file format.");

        Format = format;
    }

    /// <summary>The file's format.</summary>
    public VectorFileFormat Format { get; }

    /// <summary>
    /// The attribute columns, in order; <see langword="null"/> (the default) = every
    /// <see cref="Models.GeoFeatureProperties.CustomAttributes"/> key any feature has, in first-seen order. Given, a
    /// feature's other attributes are left out and the input is written in one pass; left out, the writer first spools
    /// the input to a temporary file on disk to learn the columns, since both formats fix their columns before the first
    /// row.
    /// </summary>
    public IReadOnlyList<string>? Attributes { get; init; }

    /// <summary>
    /// How many rows a GeoParquet file's driver buffers before writing them out as one row group, bounding what it holds
    /// in memory (a FlatGeobuf file is written row by row); within [1, <see cref="VectorFileReadRequest.MaxLimit"/>].
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set outside [1, <see cref="VectorFileReadRequest.MaxLimit"/>].</exception>
    public int BatchSize
    {
        get => _batchSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, VectorFileReadRequest.MaxLimit);
            _batchSize = value;
        }
    }
}
