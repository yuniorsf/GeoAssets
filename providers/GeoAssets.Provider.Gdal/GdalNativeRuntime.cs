using System.Runtime.InteropServices;

namespace GeoAssets.Provider.Gdal;

/// <summary>
/// Where this plugin's native GDAL can run (XD01-437): the MaxRev Minimal runtimes it ships cover macOS and Linux on
/// x64 and arm64 only. Elsewhere — Windows (no Windows runtime is referenced), iOS, Android, Mac Catalyst, the browser —
/// the plugin registers only its pure .NET parts (PMTiles) and leaves the GDAL-backed ones out.
/// </summary>
public static class GdalNativeRuntime
{
    /// <summary>Whether this platform has a native GDAL runtime in the plugin.</summary>
    public static bool IsSupportedPlatform =>
        IsSupported(
            OperatingSystem.IsLinux(),
            OperatingSystem.IsMacOS() && !OperatingSystem.IsMacCatalyst(),
            RuntimeInformation.ProcessArchitecture);

    /// <summary>The platform, for messages: e.g. <c>osx-arm64</c>.</summary>
    public static string Describe() => RuntimeInformation.RuntimeIdentifier;

    internal static bool IsSupported(bool isLinux, bool isMacOS, Architecture architecture) =>
        (isLinux || isMacOS) && architecture is Architecture.X64 or Architecture.Arm64;
}
