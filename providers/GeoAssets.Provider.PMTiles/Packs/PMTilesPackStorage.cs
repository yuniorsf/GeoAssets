using System.Globalization;
using System.Text.Json;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// Where a host keeps the offline map packs it imports (XD01-438) — on MAUI a folder of its app data, so a pack picked
/// from a file provider's temporary copy (iOS, Android) or a removable location stays readable offline and across
/// restarts. <see cref="ImportAsync(string, string?, CancellationToken)"/> validates the picked file first (a PMTiles v3
/// pack this device reads — vector, or since XD01-465 PNG/JPEG/WebP raster), then streams it into <see cref="Directory"/>
/// as <c>&lt;id&gt;.pmtiles</c> in 1 MiB blocks — never whole in memory, never row by row into a database — through a
/// temporary file renamed once complete, so an interrupted import leaves no half pack. A pack already there (same id
/// and length) isn't copied again, and a file already in the directory is opened where it is. A pack larger than
/// <see cref="LargePackBytes"/> is copied only once its id was confirmed: before that, the import answers
/// <see cref="PMTilesPackSizeConfirmationException"/> instead of copying (XD01-465). Registered with
/// <see cref="PMTilesServiceExtensions.AddPMTilesPackStorage"/>; without it the <c>pmtiles</c> plugin reads a pack where
/// it was picked.
/// <para>
/// Managing the packs (XD01-464): an import that copies a pack records the picked file's name and the import time (from
/// its <see cref="TimeProvider"/>) in <c>pack-info/&lt;id&gt;.json</c> under <see cref="Directory"/>;
/// <see cref="ListAsync"/> answers every stored pack with that record, its size and its header;
/// <see cref="Delete"/> removes a pack's file and record. A pack can be deleted while it is open: its readers keep
/// reading the deleted file until they are disposed (<see cref="PMTilesArchive"/> shares the file for deleting).
/// </para>
/// </summary>
public sealed class PMTilesPackStorage
{
    /// <summary>The extension of a stored pack.</summary>
    public const string PackExtension = ".pmtiles";

    /// <summary>The default <see cref="LargePackBytes"/>: 1 GiB.</summary>
    public const long DefaultLargePackBytes = 1024L * 1024 * 1024;

    /// <summary>The folder, under <see cref="Directory"/>, of the records of the stored packs' imports.</summary>
    public const string InfoDirectoryName = "pack-info";

    private const int CopyBufferBytes = 1024 * 1024;
    private const int PackIdLength = 16;

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// A storage keeping its packs in <paramref name="directory"/> (created on the first import), asking before it copies
    /// a pack larger than <paramref name="largePackBytes"/>, recording import times from <paramref name="timeProvider"/>
    /// (the system clock when omitted).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="largePackBytes"/> is not positive.</exception>
    public PMTilesPackStorage(string directory, long largePackBytes = DefaultLargePackBytes, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(largePackBytes);
        Directory = System.IO.Path.GetFullPath(directory);
        LargePackBytes = largePackBytes;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The folder the packs are kept in.</summary>
    public string Directory { get; }

    /// <summary>The size above which a pack is copied only once confirmed (<see cref="PMTilesPackSizeConfirmationException"/>).</summary>
    public long LargePackBytes { get; }

    /// <summary>
    /// <see cref="ImportAsync(string, string?, CancellationToken)"/> with nothing confirmed: a pack larger than
    /// <see cref="LargePackBytes"/> is not copied.
    /// </summary>
    public Task<PMTilesPack> ImportAsync(string sourcePath, CancellationToken ct) => ImportAsync(sourcePath, confirmedLargePackId: null, ct);

    /// <summary>
    /// Copies the pack at <paramref name="sourcePath"/> into <see cref="Directory"/> (unless it is there already) and opens
    /// the copy. A pack larger than <see cref="LargePackBytes"/> that isn't there yet is copied only when its id is
    /// <paramref name="confirmedLargePackId"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="sourcePath"/>.</exception>
    /// <exception cref="PMTilesFormatException">The file is not an offline map pack this device can read (see <see cref="PMTilesPack.OpenAsync"/>).</exception>
    /// <exception cref="PMTilesPackSizeConfirmationException">The pack is larger than <see cref="LargePackBytes"/> and its id wasn't confirmed; nothing was copied.</exception>
    /// <exception cref="IOException">The copy failed (e.g. the device is out of space); nothing is left in the directory.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled; nothing is left in the directory.</exception>
    public async Task<PMTilesPack> ImportAsync(string sourcePath, string? confirmedLargePackId, CancellationToken ct)
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
        {
            var length = new FileInfo(source).Length;
            if (length > LargePackBytes && !string.Equals(confirmedLargePackId, id, StringComparison.Ordinal))
                throw new PMTilesPackSizeConfirmationException(System.IO.Path.GetFileName(source), id, length, LargePackBytes);

            await CopyAsync(source, destination, ct).ConfigureAwait(false);
            await RecordImportAsync(id, System.IO.Path.GetFileName(source)).ConfigureAwait(false);
        }

        return await PMTilesPack.OpenAsync(destination, ct).ConfigureAwait(false);
    }

    /// <summary>Whether <paramref name="value"/> is a pack id (<see cref="PMTilesPack.Id"/>: 16 lowercase hex digits).</summary>
    public static bool IsPackId(string? value) =>
        value is { Length: PackIdLength } && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>Where the pack <paramref name="packId"/> is (or would be) stored.</summary>
    /// <exception cref="ArgumentException"><paramref name="packId"/> is not a pack id (<see cref="IsPackId"/>).</exception>
    public string PathOf(string packId)
    {
        ArgumentNullException.ThrowIfNull(packId);
        if (!IsPackId(packId))
            throw new ArgumentException($"'{packId}' is not an offline map pack id.", nameof(packId));
        return System.IO.Path.Combine(Directory, packId + PackExtension);
    }

    /// <summary>
    /// Every pack stored in <see cref="Directory"/> (a <c>&lt;id&gt;.pmtiles</c> file — an import under way isn't one
    /// yet), newest import first, with its import record, size and header.
    /// </summary>
    public async Task<IReadOnlyList<PMTilesStoredPack>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!System.IO.Directory.Exists(Directory))
            return [];

        var packs = new List<PMTilesStoredPack>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + PackExtension))
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(path);
            if (IsPackId(id) && await DescribeAsync(id, path, ct).ConfigureAwait(false) is { } pack)
                packs.Add(pack);
        }

        return [.. packs.OrderByDescending(p => p.ImportedAt).ThenBy(p => p.Id, StringComparer.Ordinal)];
    }

    /// <summary>The bytes the stored packs use together.</summary>
    public async Task<long> GetUsedBytesAsync(CancellationToken ct) =>
        (await ListAsync(ct).ConfigureAwait(false)).Sum(p => p.SizeBytes);

    /// <summary>
    /// Deletes the pack <paramref name="packId"/>'s file and its import record. Open readers of the pack keep reading
    /// the deleted file until they are disposed.
    /// </summary>
    /// <returns>Whether the pack was stored.</returns>
    /// <exception cref="ArgumentException"><paramref name="packId"/> is not a pack id (<see cref="IsPackId"/>).</exception>
    /// <exception cref="IOException">The file couldn't be deleted.</exception>
    public bool Delete(string packId)
    {
        var path = PathOf(packId);
        var existed = File.Exists(path);
        if (existed)
            File.Delete(path);
        TryDelete(InfoPathOf(packId));
        return existed;
    }

    private static bool IsSameFile(string destination, string source) =>
        File.Exists(destination) && new FileInfo(destination).Length == new FileInfo(source).Length;

    private string InfoPathOf(string packId) => System.IO.Path.Combine(Directory, InfoDirectoryName, packId + ".json");

    private async Task<PMTilesStoredPack?> DescribeAsync(string id, string path, CancellationToken ct)
    {
        var header = await ReadHeaderAsync(path, ct).ConfigureAwait(false);
        var (fileName, importedAt) = await ReadImportAsync(id, ct).ConfigureAwait(false);
        var file = new FileInfo(path);
        if (!file.Exists)
            return null;

        return new PMTilesStoredPack(
            id,
            file.FullName,
            fileName,
            file.Length,
            importedAt ?? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            header);
    }

    private static async Task<PMTilesHeader?> ReadHeaderAsync(string path, CancellationToken ct)
    {
        try
        {
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
            var bytes = new byte[PMTilesHeader.Size];
            var filled = 0;
            while (filled < bytes.Length)
            {
                var read = await RandomAccess.ReadAsync(file, bytes.AsMemory(filled), filled, ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                filled += read;
            }

            return PMTilesHeader.Parse(bytes.AsSpan(0, filled));
        }
        catch (PMTilesFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<(string? FileName, DateTimeOffset? ImportedAt)> ReadImportAsync(string packId, CancellationToken ct)
    {
        var path = InfoPathOf(packId);
        if (!File.Exists(path))
            return (null, null);

        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null);

            var fileName = root.TryGetProperty("fileName", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
            DateTimeOffset? importedAt = root.TryGetProperty("importedAt", out var at) && at.ValueKind == JsonValueKind.String
                                         && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
            return (fileName, importedAt);
        }
        catch (JsonException)
        {
            return (null, null);
        }
        catch (IOException)
        {
            return (null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    /// <summary>Records the import of <paramref name="packId"/> — best effort: a pack without a record lists its file's last write instead.</summary>
    private async Task RecordImportAsync(string packId, string fileName)
    {
        var path = InfoPathOf(packId);
        var partial = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await using (output.ConfigureAwait(false))
            {
                var json = new Utf8JsonWriter(output);
                await using (json.ConfigureAwait(false))
                {
                    json.WriteStartObject();
                    json.WriteString("fileName", fileName);
                    json.WriteString("importedAt", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
                    json.WriteEndObject();
                    await json.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            File.Move(partial, path, overwrite: true);
        }
        catch (IOException)
        {
            TryDelete(partial);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(partial);
        }
    }

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
