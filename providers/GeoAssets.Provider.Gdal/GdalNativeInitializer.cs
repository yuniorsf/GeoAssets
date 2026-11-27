using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using GeoAssets.Core.Plugins;
using MaxRev.Gdal.Core;
using OSGeo.OSR;

namespace GeoAssets.Provider.Gdal;

/// <summary>
/// The one place native GDAL is configured (ADR-002 D5, XD01-437): every GDAL-backed reader, writer and tile source —
/// and <c>GeoAssets.Ingestion.Proj</c>'s PROJ transformer — calls <see cref="EnsureInitialized"/> before its first
/// GDAL/OGR/OSR call, and nothing else in the repository calls <c>GdalBase.ConfigureAll()</c>
/// (<c>GdalNativeBoundaryTests</c>).
/// <para>
/// <b>Once per process.</b> The first call claims the native library <see cref="NativeLibraryName"/> for this assembly
/// in <see cref="NativeLibraryOwnership"/> (one registry per process, in the shared <c>GeoAssets.Core</c>), then runs
/// <c>GdalBase.ConfigureAll()</c> under a lock; concurrent first calls wait for it and every later call returns at
/// once. A second copy of this assembly — loaded both statically and from a plugin directory, or from two plugin
/// directories — is refused with <see cref="NativeLibraryOwnershipException"/> before it touches GDAL, since each copy
/// has its own statics over the one native library.
/// </para>
/// <para>
/// <b>Failure.</b> Where the native runtime can't load (no MaxRev runtime for the platform — Windows, iOS, Android — or
/// a broken install) the first call throws <see cref="GdalNativeUnavailableException"/>, and so does every later one,
/// without trying again.
/// </para>
/// </summary>
public static class GdalNativeInitializer
{
    /// <summary>The name this assembly claims in <see cref="NativeLibraryOwnership"/>.</summary>
    public const string NativeLibraryName = "gdal";

    private static readonly Lock Gate = new();
    private static volatile bool _initialized;
    private static int _configurationCount;
    private static string[] _initialProjSearchPaths = [];
    private static string? _nativeVersion;
    private static ExceptionDispatchInfo? _failure;

    /// <summary>Whether GDAL is configured in this process by this copy.</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>How many times this copy ran <c>GdalBase.ConfigureAll()</c>: 0 before the first successful call, 1 after.</summary>
    public static int ConfigurationCount => Volatile.Read(ref _configurationCount);

    /// <summary>The PROJ search paths right after configuration (where <c>proj.db</c> is), before anyone prepends a grid folder.</summary>
    public static IReadOnlyList<string> InitialProjSearchPaths => _initialProjSearchPaths;

    /// <summary>The native GDAL release (e.g. <c>3.13.3</c>); <see langword="null"/> before initialization.</summary>
    public static string? NativeVersion => _nativeVersion;

    /// <summary>Configures native GDAL (drivers, <c>GDAL_DATA</c>, PROJ) if this process hasn't yet; safe from any thread.</summary>
    /// <exception cref="NativeLibraryOwnershipException">Another copy of this assembly owns native GDAL in this process.</exception>
    /// <exception cref="GdalNativeUnavailableException">The native GDAL runtime can't be loaded here.</exception>
    public static void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (Gate)
        {
            if (_initialized)
                return;

            _failure?.Throw();
            NativeLibraryOwnership.Claim(NativeLibraryName, typeof(GdalNativeInitializer).Assembly);
            try
            {
                ConfigureNative();
            }
            catch (Exception ex) when (IsNativeLoadFailure(ex))
            {
                _failure = ExceptionDispatchInfo.Capture(new GdalNativeUnavailableException(
                    $"Native GDAL can't be loaded on {GdalNativeRuntime.Describe()}: {ex.Message}", ex));
                _failure.Throw();
            }

            Interlocked.Increment(ref _configurationCount);
            _initialized = true;
        }
    }

    /// <summary>
    /// The only code that touches GDAL — kept out of <see cref="EnsureInitialized"/> (and never inlined into it), so a
    /// refused copy never even loads GDAL's managed bindings.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConfigureNative()
    {
        GdalBase.ConfigureAll();
        _initialProjSearchPaths = Osr.GetPROJSearchPaths() ?? [];
        _nativeVersion = OSGeo.GDAL.Gdal.VersionInfo("RELEASE_NAME");
    }

    private static bool IsNativeLoadFailure(Exception ex) =>
        ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or TypeInitializationException
            or FileNotFoundException or PlatformNotSupportedException;
}
