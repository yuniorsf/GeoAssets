using GeoAssets.Hosting.Abstractions.Plugins;

namespace GeoAssets.Hosting.Abstractions.Tests.Plugins;

/// <summary>A temporary plugin directory, deleted on dispose.</summary>
internal sealed class TempPluginDirectory : IDisposable
{
    public TempPluginDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "geoassets-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>
    /// Copies a fixture plugin's build output to <c>&lt;Root&gt;/&lt;folderName&gt;/</c> (only the running platform's
    /// <c>runtimes/</c> folders), optionally without its <c>.deps.json</c>; returns the folder.
    /// </summary>
    public string CopyPlugin(string plugin, string? folderName = null, bool withDepsJson = true)
    {
        var target = Path.Combine(Root, folderName ?? plugin);
        CopyOutput(plugin, target, withDepsJson);
        return target;
    }

    /// <summary>Copies a fixture plugin's build output into the root, its entry assembly renamed <paramref name="entryFileName"/>, without its <c>.deps.json</c>.</summary>
    public string CopyPluginFlat(string plugin, string entryFileName)
    {
        CopyOutput(plugin, Root, withDepsJson: false);
        var entry = Path.Combine(Root, entryFileName);
        File.Move(Path.Combine(Root, plugin + ".dll"), entry);
        return entry;
    }

    public string CreateFolder(string relativePath) => Directory.CreateDirectory(Path.Combine(Root, relativePath)).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A loaded plugin can keep its files open on some platforms; the OS temp cleanup removes them.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    private static void CopyOutput(string plugin, string target, bool withDepsJson)
    {
        var source = PluginFixtures.Folder(plugin);
        var currentRuntimes = NativeLibraryProbe.CurrentRuntimeIdentifiers()
            .Select(rid => Path.Combine(source, "runtimes", rid) + Path.DirectorySeparatorChar)
            .ToList();
        var runtimesFolder = Path.Combine(source, "runtimes") + Path.DirectorySeparatorChar;

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (file.StartsWith(runtimesFolder, StringComparison.Ordinal)
                && !currentRuntimes.Exists(rid => file.StartsWith(rid, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!withDepsJson && file.EndsWith(".deps.json", StringComparison.Ordinal))
                continue;

            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
