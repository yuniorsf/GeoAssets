using System.Runtime.InteropServices;
using Xunit;

namespace GeoAssets.Provider.Gdal.PluginLoading.Tests;

/// <summary>
/// A fact that needs the native GDAL runtime the plugin ships — MaxRev's Minimal runtimes for macOS and Linux on x64 and
/// arm64. Elsewhere it is reported as skipped, with the reason, instead of passing.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class NativeGdalFactAttribute : FactAttribute
{
    public NativeGdalFactAttribute()
    {
        var supported = (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                        && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
        if (!supported)
            Skip = $"GeoAssets.Provider.Gdal ships no native GDAL runtime for {RuntimeInformation.RuntimeIdentifier}.";
    }
}
