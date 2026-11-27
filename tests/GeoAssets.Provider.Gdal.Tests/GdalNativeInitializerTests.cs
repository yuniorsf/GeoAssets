using System.Runtime.InteropServices;
using System.Runtime.Loader;
using FluentAssertions;
using GeoAssets.Core.Plugins;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests;

/// <summary>
/// <see cref="GdalNativeInitializer"/> with the plugin referenced statically (default load context). Its first-use
/// concurrency in a fresh copy, and the refusal of a second copy, are proven in a host that loads the plugin from a
/// directory (<c>GeoAssets.Provider.Gdal.PluginLoading.Tests</c>); here other tests of this process may already have
/// configured GDAL, so the assertions hold whatever ran first.
/// </summary>
public sealed class GdalNativeInitializerTests
{
    [Fact]
    public async Task ConcurrentCalls_ConfigureGdalOnce()
    {
        using var start = new Barrier(16);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            GdalNativeInitializer.EnsureInitialized();
        })));

        GdalNativeInitializer.IsInitialized.Should().BeTrue();
        GdalNativeInitializer.ConfigurationCount.Should().Be(1);
    }

    [Fact]
    public void Initialization_ClaimsNativeGdalForThisAssembly_InTheDefaultContext()
    {
        GdalNativeInitializer.EnsureInitialized();

        var owner = NativeLibraryOwnership.OwnerOf(GdalNativeInitializer.NativeLibraryName);
        owner.Should().BeSameAs(typeof(GdalNativeInitializer).Assembly);
        AssemblyLoadContext.GetLoadContext(owner!).Should().BeSameAs(AssemblyLoadContext.Default);
    }

    [Fact]
    public void Initialization_ConfiguresProjAndReportsTheNativeRelease()
    {
        GdalNativeInitializer.EnsureInitialized();

        GdalNativeInitializer.NativeVersion.Should().StartWith("3.");
        GdalNativeInitializer.InitialProjSearchPaths.Should().Contain(p => File.Exists(Path.Combine(p, "proj.db")));
    }

    [Theory]
    [InlineData(true, false, Architecture.X64, true)]
    [InlineData(true, false, Architecture.Arm64, true)]
    [InlineData(false, true, Architecture.Arm64, true)]
    [InlineData(false, true, Architecture.X64, true)]
    [InlineData(false, false, Architecture.X64, false)]
    [InlineData(true, false, Architecture.Arm, false)]
    [InlineData(false, false, Architecture.Wasm, false)]
    public void NativeRuntime_CoversMacOSAndLinux_OnX64AndArm64(bool linux, bool macOS, Architecture architecture, bool expected)
    {
        GdalNativeRuntime.IsSupported(linux, macOS, architecture).Should().Be(expected);
    }

    [Fact]
    public void NativeRuntime_IsSupportedWhereTheseTestsRun()
    {
        GdalNativeRuntime.IsSupportedPlatform.Should().Be(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        GdalNativeRuntime.Describe().Should().Be(RuntimeInformation.RuntimeIdentifier);
    }
}
