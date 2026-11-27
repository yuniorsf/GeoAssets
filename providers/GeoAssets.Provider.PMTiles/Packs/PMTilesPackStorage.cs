namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// Where a host keeps the offline map packs it imports (XD01-438) — on MAUI a folder of its app data, so a pack picked
/// from a file provider's temporary copy (iOS, Android) or a removable location stays readable offline and across
/// restarts. <see cref="ImportAsync"/> validates the picked file first (a PMTiles v3 vector pack this device decodes),
/// then streams it into <see cref="Directory"/> as <c>&lt;id&gt;.pmtiles</c> in 1 MiB blocks — never whole in memory, never
/// row by row into a database — through a temporary file renamed once complete, so an interrupted import leaves no
/// half pack. A pack already there (same id and length) isn't copied again, and a file already in the directory is
/// opened where it is. Registered with <see cref="PMTilesServiceExtensions.AddPMTilesPackStorage"/>; without it the
/// <c>pmtiles</c> plugin reads a pack where it was picked.
/// </summary>
public sealed class PMTilesPackStorage
{
    /// <summary>The extension of a stored pack.</summary>
    public const string PackExtension = ".pmtiles";

    private const int CopyBufferBytes = 1024 * 1024;

    /// <summary>A storage keeping its packs in <paramref name="directory"/> (created on the first import).</summary>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is blank.</exception>
    public PMTilesPackStorage(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = System.IO.Path.GetFullPath(directory);
    }

    /// <summary>The folder the packs are kept in.</summary>
    public string Directory { get; }

    /// <summary>Copies the pack at <paramref name="sourcePath"/> into <see cref="Directory"/> (unless it is there already) and opens the copy.</summary>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="sourcePath"/>.</exception>
    /// <exception cref="PMTilesFormatException">The file is not an offline map pack this device can read (see <see cref="PMTilesPack.OpenAsync"/>).</exception>
    /// <exception cref="IOException">The copy failed (e.g. the device is out of space); nothing is left in the directory.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled; nothing is left in the directory.</exception>
    public async Task<PMTilesPack> ImportAsync(string sourcePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var source = System.IO.Path.GetFullPath(sourcePath);
        if (string.Equals(System.IO.Path.GetDirectoryName(source), Directory, StringComparison.Ordinal))
            return await PMTilesPack.OpenAsync(source, ct).ConfigureAwait(false);

        string id;
        using (var picked = await PMTilesPack.OpenAsync(source, ct).ConfigureAwait(false))
            id = picked.Id;

        var destination = System.IO.Path.Combine(Directory, id + PackExtension);
        if (!IsSameFile(destination, source))
            await CopyAsync(source, destination, ct).ConfigureAwait(false);

        return await PMTilesPack.OpenAsync(destination, ct).ConfigureAwait(false);
    }

    private static bool IsSameFile(string destination, string source) =>
        File.Exists(destination) && new FileInfo(destination).Length == new FileInfo(source).Length;

    private async Task CopyAsync(string source, string destination, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (input.ConfigureAwait(false))
            {
                var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferBytes, FileOptions.Asynchronous);
                await using (output.ConfigureAwait(false))
                {
                    await input.CopyToAsync(output, CopyBufferBytes, ct).ConfigureAwait(false);
                    await output.FlushAsync(ct).ConfigureAwait(false);
                }
            }

            File.Move(partial, destination, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
