using System.Collections.Concurrent;
using System.Reflection;

namespace GeoAssets.Core.Plugins;

/// <summary>
/// The process-wide record of which managed assembly owns the bindings of a native library (ADR-002 D5, XD01-437).
/// A native library is loaded once per process, whichever <see cref="System.Runtime.Loader.AssemblyLoadContext"/> asks
/// for it, but every load context that loads a binding assembly gets its own copy of that assembly's static state — so
/// two copies (one statically referenced by a host, one loaded from its plugin directory; or one plugin loaded twice)
/// would each believe they own and configure the library. This registry lives in <c>GeoAssets.Core</c>, which every
/// load context shares (D3), so there is one per process: the first copy to <see cref="Claim"/> a library owns it, and
/// any other copy is refused.
/// </summary>
public static class NativeLibraryOwnership
{
    private static readonly ConcurrentDictionary<string, Assembly> Owners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records <paramref name="owner"/> as the owner of <paramref name="libraryName"/> (case-insensitive), unless it
    /// already is; atomic, so of two copies claiming at once exactly one wins.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="libraryName"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    /// <exception cref="NativeLibraryOwnershipException">Another assembly — another copy, in another load context — owns the library.</exception>
    public static void Claim(string libraryName, Assembly owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryName);
        ArgumentNullException.ThrowIfNull(owner);

        var existing = Owners.GetOrAdd(libraryName, owner);
        if (!ReferenceEquals(existing, owner))
            throw new NativeLibraryOwnershipException(libraryName, owner, existing);
    }

    /// <summary>The assembly that owns <paramref name="libraryName"/>, or <see langword="null"/> when none claimed it.</summary>
    /// <exception cref="ArgumentException"><paramref name="libraryName"/> is blank.</exception>
    public static Assembly? OwnerOf(string libraryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryName);
        return Owners.TryGetValue(libraryName, out var owner) ? owner : null;
    }
}
