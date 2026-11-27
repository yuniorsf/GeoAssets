using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.Loader;

namespace GeoAssets.Hosting.Abstractions.Plugins;

/// <summary>
/// The load context of one server-side provider plugin (ADR-002 D2): the plugin's entry assembly and its private
/// dependencies load here, resolved from the plugin's own folder through its <c>.deps.json</c>
/// (<see cref="AssemblyDependencyResolver"/>), so two plugins can carry different versions of the same library.
/// <para>
/// <b>What comes from the host instead (D3).</b> An assembly the host has — one of its trusted platform assemblies
/// (its own output and the shared framework) or one already loaded in <see cref="AssemblyLoadContext.Default"/> — is
/// taken from the host, never loaded a second time here, so the types that cross the plugin boundary are the host's:
/// <list type="bullet">
/// <item>the shared contracts — <see cref="SharedContractAssemblies"/> (<c>GeoAssets.Core</c>, <c>GeoAssets.Identity</c>,
/// <c>GeoAssets.Hosting.Abstractions</c>) and every <c>Microsoft.Extensions.*</c> assembly — whatever version the plugin
/// asks for: a plugin built against a newer contract than the host's fails to load rather than get a second copy;</item>
/// <item>any other assembly (e.g. <c>NetTopologySuite</c>, whose geometries travel in <c>GeoFeature</c>) when the
/// host's version is at least the one the plugin asks for; otherwise the plugin's own copy.</item>
/// </list>
/// An assembly the host doesn't have comes from the plugin's folder.
/// </para>
/// <para>
/// <b>Native libraries</b> (<see cref="LoadUnmanagedDll"/>, e.g. GDAL) resolve from the plugin's folder, in this order:
/// the <c>.deps.json</c>'s native assets for the running runtime identifier (the <c>runtimes/&lt;rid&gt;/native/</c>
/// files a NuGet package brings); then, without such an entry, the plugin folder itself, then
/// <c>runtimes/&lt;rid&gt;/native/</c> for the running RID and its portable <c>&lt;os&gt;-&lt;arch&gt;</c> form — trying the
/// platform's file-name forms (<c>gdal</c> → <c>libgdal.so</c>, <c>libgdal.dylib</c>, <c>gdal.dll</c>). A library
/// found nowhere there falls back to the runtime's default probing.
/// </para>
/// Not collectible: a plugin stays loaded for the life of the process.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private const string SharedContractPrefix = "Microsoft.Extensions.";

    private static readonly FrozenSet<string> SharedContracts = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "GeoAssets.Core", "GeoAssets.Identity", "GeoAssets.Hosting.Abstractions");

    private static readonly Lazy<FrozenDictionary<string, string>> TrustedPlatformAssemblies =
        new(ReadTrustedPlatformAssemblies);

    private static readonly ConcurrentDictionary<string, Version?> TrustedAssemblyVersions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly AssemblyDependencyResolver _resolver;
    private readonly ConcurrentQueue<string> _loadedNativeLibraries = new();

    /// <param name="pluginMainAssemblyPath">The plugin's entry assembly; its folder is the plugin's folder.</param>
    /// <exception cref="ArgumentException"><paramref name="pluginMainAssemblyPath"/> is blank.</exception>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="pluginMainAssemblyPath"/>.</exception>
    /// <exception cref="InvalidOperationException">The plugin's <c>.deps.json</c> can't be read.</exception>
    public PluginLoadContext(string pluginMainAssemblyPath)
        : base(ContextName(pluginMainAssemblyPath), isCollectible: false)
    {
        var fullPath = Path.GetFullPath(pluginMainAssemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Provider plugin assembly '{fullPath}' does not exist.", fullPath);

        PluginMainAssemblyPath = fullPath;
        PluginDirectory = Path.GetDirectoryName(fullPath)!;
        _resolver = new AssemblyDependencyResolver(fullPath);
    }

    /// <summary>
    /// The assemblies always taken from the host, besides every <c>Microsoft.Extensions.*</c> one: the contracts a
    /// plugin is built against and hands back to the host.
    /// </summary>
    public static IReadOnlyCollection<string> SharedContractAssemblies => SharedContracts;

    /// <summary>The plugin's entry assembly (full path).</summary>
    public string PluginMainAssemblyPath { get; }

    /// <summary>The plugin's folder: where its private dependencies and native libraries are resolved from.</summary>
    public string PluginDirectory { get; }

    /// <summary>
    /// The native libraries this context resolved from the plugin's folder and loaded (<see cref="LoadUnmanagedDll"/>),
    /// full paths in load order — what a native plugin (e.g. GDAL, XD01-437) actually runs; a library left to the
    /// runtime's default probing is not listed.
    /// </summary>
    public IReadOnlyCollection<string> LoadedNativeLibraries => [.. _loadedNativeLibraries];

    /// <summary>Whether <paramref name="assemblyName"/> is a shared contract — always taken from the host.</summary>
    public static bool IsSharedContract(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        var name = assemblyName.Name;
        return name is not null
            && (SharedContracts.Contains(name) || name.StartsWith(SharedContractPrefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Loads the plugin's entry assembly into this context.</summary>
    public Assembly LoadPluginAssembly() => LoadFromAssemblyPath(PluginMainAssemblyPath);

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        if (UsesHostCopy(assemblyName, HostVersion(assemblyName)))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <inheritdoc />
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = ResolveUnmanagedDllPath(unmanagedDllName);
        if (path is null)
            return IntPtr.Zero;

        var handle = LoadUnmanagedDllFromPath(path);
        _loadedNativeLibraries.Enqueue(path);
        return handle;
    }

    /// <summary>Where <see cref="LoadUnmanagedDll"/> finds <paramref name="unmanagedDllName"/>, or <see langword="null"/>.</summary>
    internal string? ResolveUnmanagedDllPath(string unmanagedDllName) =>
        _resolver.ResolveUnmanagedDllToPath(unmanagedDllName)
        ?? NativeLibraryProbe.Probe(PluginDirectory, unmanagedDllName, NativeLibraryProbe.CurrentPlatform, NativeLibraryProbe.CurrentRuntimeIdentifiers());

    /// <summary>
    /// The sharing rule: whether a request for <paramref name="requested"/> is answered with the host's copy, given the
    /// version the host has (<see langword="null"/> when it has none).
    /// </summary>
    internal static bool UsesHostCopy(AssemblyName requested, Version? hostVersion)
    {
        if (hostVersion is null)
            return false;
        if (IsSharedContract(requested))
            return true;

        return requested.Version is null || hostVersion >= requested.Version;
    }

    /// <summary>The version of <paramref name="assemblyName"/> the host has, or <see langword="null"/> when it has none.</summary>
    internal static Version? HostVersion(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null)
            return null;

        foreach (var loaded in Default.Assemblies)
        {
            var loadedName = loaded.GetName();
            if (string.Equals(loadedName.Name, name, StringComparison.OrdinalIgnoreCase))
                return loadedName.Version ?? new Version(0, 0);
        }

        return TrustedPlatformAssemblies.Value.TryGetValue(name, out var path)
            ? TrustedAssemblyVersions.GetOrAdd(path, ReadVersion)
            : null;
    }

    private static Version? ReadVersion(string path)
    {
        try
        {
            return AssemblyName.GetAssemblyName(path).Version ?? new Version(0, 0);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FrozenDictionary<string, string> ReadTrustedPlatformAssemblies()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string list)
        {
            foreach (var path in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                byName.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }

        return byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static string ContextName(string pluginMainAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginMainAssemblyPath);
        return $"GeoAssets.ProviderPlugin:{Path.GetFileNameWithoutExtension(pluginMainAssemblyPath)}";
    }
}
