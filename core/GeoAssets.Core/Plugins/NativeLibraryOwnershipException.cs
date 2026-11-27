using System.Reflection;
using System.Runtime.Loader;

namespace GeoAssets.Core.Plugins;

/// <summary>
/// A second copy of a native library's managed bindings tried to claim it (<see cref="NativeLibraryOwnership.Claim"/>):
/// the host loads them both statically and from a plugin directory, or loads one plugin twice. A host chooses one
/// loading model (ADR-002 D5, XD01-437).
/// </summary>
public sealed class NativeLibraryOwnershipException : InvalidOperationException
{
    /// <summary>A refusal of <paramref name="claimant"/>'s claim on <paramref name="libraryName"/>, which <paramref name="owner"/> holds.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="claimant"/> or <paramref name="owner"/> is <see langword="null"/>.</exception>
    public NativeLibraryOwnershipException(string libraryName, Assembly claimant, Assembly owner)
        : base(Describe(libraryName, claimant, owner))
    {
        LibraryName = libraryName;
        ClaimantLoadContext = LoadContextName(claimant);
        OwnerLoadContext = LoadContextName(owner);
    }

    public NativeLibraryOwnershipException()
    {
    }

    public NativeLibraryOwnershipException(string message) : base(message)
    {
    }

    public NativeLibraryOwnershipException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The native library both copies claim; empty when built with a standard constructor.</summary>
    public string LibraryName { get; } = string.Empty;

    /// <summary>The refused copy's load context, e.g. <c>Default</c> or <c>GeoAssets.ProviderPlugin:X</c>; empty when unknown.</summary>
    public string ClaimantLoadContext { get; } = string.Empty;

    /// <summary>The owning copy's load context; empty when unknown.</summary>
    public string OwnerLoadContext { get; } = string.Empty;

    private static string Describe(string libraryName, Assembly claimant, Assembly owner)
    {
        ArgumentNullException.ThrowIfNull(claimant);
        ArgumentNullException.ThrowIfNull(owner);

        return $"The native library '{libraryName}' is already owned by {owner.GetName().Name} in load context " +
               $"'{LoadContextName(owner)}'; a second copy of {claimant.GetName().Name} in load context " +
               $"'{LoadContextName(claimant)}' may not configure it again. A host loads these bindings one way only — " +
               "statically referenced, or as a plugin from its plugin directory, never both (ADR-002 D5).";
    }

    private static string LoadContextName(Assembly assembly) =>
        AssemblyLoadContext.GetLoadContext(assembly)?.Name ?? string.Empty;
}
