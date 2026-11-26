using System.Runtime.InteropServices;

namespace GeoAssets.Hosting.Abstractions.Plugins;

/// <summary>
/// <see cref="PluginLoadContext"/>'s fallback for a native library the plugin's <c>.deps.json</c> doesn't list:
/// looks in the plugin folder, then <c>runtimes/&lt;rid&gt;/native/</c> for each runtime identifier given, for each
/// platform file-name form of the library.
/// </summary>
internal static class NativeLibraryProbe
{
    internal static NativePlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? NativePlatform.Windows
        : OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? NativePlatform.MacOS
        : NativePlatform.Linux;

    /// <summary>The running runtime identifier, then its portable <c>&lt;os&gt;-&lt;arch&gt;</c> form when that differs.</summary>
    internal static IReadOnlyList<string> CurrentRuntimeIdentifiers()
    {
        var os = CurrentPlatform switch
        {
            NativePlatform.Windows => "win",
            NativePlatform.MacOS => "osx",
            _ => "linux",
        };
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.Armv6 => "armv6",
            Architecture.S390x => "s390x",
            Architecture.LoongArch64 => "loongarch64",
            Architecture.Ppc64le => "ppc64le",
            Architecture.RiscV64 => "riscv64",
            _ => "wasm",
        };
        var portable = $"{os}-{arch}";

        return string.Equals(RuntimeInformation.RuntimeIdentifier, portable, StringComparison.OrdinalIgnoreCase)
            ? [portable]
            : [RuntimeInformation.RuntimeIdentifier, portable];
    }

    /// <summary>The first existing file for <paramref name="libraryName"/> under <paramref name="pluginDirectory"/>, or <see langword="null"/>.</summary>
    internal static string? Probe(
        string pluginDirectory, string libraryName, NativePlatform platform, IReadOnlyList<string> runtimeIdentifiers)
    {
        if (string.IsNullOrWhiteSpace(libraryName) || Path.IsPathRooted(libraryName)
            || libraryName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || libraryName.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        var fileNames = CandidateFileNames(libraryName, platform);
        foreach (var directory in ProbeDirectories(pluginDirectory, runtimeIdentifiers))
        {
            foreach (var fileName in fileNames)
            {
                var path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    /// <summary>The folders probed, in order: the plugin folder, then <c>runtimes/&lt;rid&gt;/native</c> per identifier.</summary>
    internal static IReadOnlyList<string> ProbeDirectories(string pluginDirectory, IReadOnlyList<string> runtimeIdentifiers)
    {
        var directories = new List<string> { pluginDirectory };
        foreach (var rid in runtimeIdentifiers)
            directories.Add(Path.Combine(pluginDirectory, "runtimes", rid, "native"));
        return directories;
    }

    /// <summary>
    /// The file names <paramref name="libraryName"/> may have on <paramref name="platform"/>, in order: with the
    /// platform's suffix, with the <c>lib</c> prefix (not on Windows), then as given.
    /// </summary>
    internal static IReadOnlyList<string> CandidateFileNames(string libraryName, NativePlatform platform)
    {
        var suffix = platform switch
        {
            NativePlatform.Windows => ".dll",
            NativePlatform.MacOS => ".dylib",
            _ => ".so",
        };
        var hasSuffix = libraryName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || (platform == NativePlatform.Linux && libraryName.Contains(".so.", StringComparison.Ordinal));
        var prefixed = platform != NativePlatform.Windows
            && !libraryName.StartsWith("lib", StringComparison.Ordinal);

        var names = new List<string>();
        if (!hasSuffix)
        {
            names.Add(libraryName + suffix);
            if (prefixed)
                names.Add("lib" + libraryName + suffix);
        }

        names.Add(libraryName);
        if (prefixed)
            names.Add("lib" + libraryName);
        return names;
    }
}
