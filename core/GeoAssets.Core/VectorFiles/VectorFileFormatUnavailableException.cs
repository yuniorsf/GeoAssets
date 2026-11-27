namespace GeoAssets.Core.VectorFiles;

/// <summary>
/// The runtime this process loaded has no driver for a vector file format (XD01-437) — e.g. a GDAL build without the
/// Parquet driver, or a platform without the native GDAL runtime at all. Thrown when the format is used, never at
/// startup.
/// </summary>
public sealed class VectorFileFormatUnavailableException : NotSupportedException
{
    /// <summary><paramref name="format"/> can't be read: <paramref name="reason"/>.</summary>
    public VectorFileFormatUnavailableException(VectorFileFormat format, string reason)
        : base($"{format} files can't be read or written here: {reason}")
    {
        Format = format;
    }

    /// <summary><paramref name="format"/> can't be read because of <paramref name="innerException"/>.</summary>
    public VectorFileFormatUnavailableException(VectorFileFormat format, string reason, Exception innerException)
        : base($"{format} files can't be read or written here: {reason}", innerException)
    {
        Format = format;
    }

    public VectorFileFormatUnavailableException()
    {
    }

    public VectorFileFormatUnavailableException(string message) : base(message)
    {
    }

    public VectorFileFormatUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The format that can't be read; <c>default</c> when built with a standard constructor.</summary>
    public VectorFileFormat Format { get; }
}
