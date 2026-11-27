using System.Globalization;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// A pack is larger than <see cref="PMTilesPackStorage.LargePackBytes"/>, so <see cref="PMTilesPackStorage.ImportAsync(string, string?, CancellationToken)"/>
/// asks before copying it onto the device (XD01-465): nothing was copied. Its <see cref="Exception.Message"/> is written
/// for the field user; importing <see cref="PackId"/> again with it as the confirmed id copies it.
/// </summary>
public sealed class PMTilesPackSizeConfirmationException : Exception
{
    public PMTilesPackSizeConfirmationException()
    {
    }

    public PMTilesPackSizeConfirmationException(string message) : base(message)
    {
    }

    public PMTilesPackSizeConfirmationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>The request to confirm importing the pack <paramref name="packId"/> of <paramref name="packBytes"/> bytes picked as <paramref name="fileName"/>.</summary>
    public PMTilesPackSizeConfirmationException(string fileName, string packId, long packBytes, long largePackBytes)
        : base(MessageFor(fileName, packBytes))
    {
        FileName = fileName;
        PackId = packId;
        PackBytes = packBytes;
        LargePackBytes = largePackBytes;
    }

    /// <summary>The picked file's name.</summary>
    public string FileName { get; } = string.Empty;

    /// <summary>The pack's <see cref="PMTilesPack.Id"/> — what confirms importing it.</summary>
    public string PackId { get; } = string.Empty;

    /// <summary>The pack's size.</summary>
    public long PackBytes { get; }

    /// <summary>The size above which an import asks first.</summary>
    public long LargePackBytes { get; }

    /// <summary><paramref name="bytes"/> for a person: <c>820 MB</c>, <c>3.4 GB</c> (1 KB = 1,024 bytes).</summary>
    public static string FormatSize(long bytes)
    {
        const double mebibyte = 1024d * 1024d;
        const double gibibyte = mebibyte * 1024d;
        return bytes >= gibibyte
            ? (bytes / gibibyte).ToString("0.#", CultureInfo.InvariantCulture) + " GB"
            : Math.Max(1, Math.Round(bytes / mebibyte)).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }

    private static string MessageFor(string fileName, long packBytes)
    {
        var size = FormatSize(packBytes);
        return $"'{fileName}' is a large offline map pack ({size}). Importing it copies it onto this device: it uses {size} of storage "
               + "and can take several minutes, so keep the app open until it is done. Press Connect again to import it.";
    }
}
