using System.Text.Json;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>Reads and (atomically) rewrites the checkpoint file of an import.</summary>
internal sealed class ImportCheckpointStore(string path)
{
    /// <summary>The last acknowledged sequence of <paramref name="importId"/>, or -1 (no file, another import, unreadable).</summary>
    public async Task<int> LastSequenceAsync(string importId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return -1;
        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                var checkpoint = await JsonSerializer.DeserializeAsync<ImportCheckpoint>(stream, NetworkImportJson.Wire, cancellationToken).ConfigureAwait(false);
                return checkpoint is not null && string.Equals(checkpoint.ImportId, importId, StringComparison.Ordinal) ? checkpoint.LastSequence : -1;
            }
        }
        catch (JsonException)
        {
            return -1;
        }
    }

    public async Task SaveAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        var stream = File.Create(temporary);
        await using (stream.ConfigureAwait(false))
            await JsonSerializer.SerializeAsync(stream, checkpoint, NetworkImportJson.Wire, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }
}
