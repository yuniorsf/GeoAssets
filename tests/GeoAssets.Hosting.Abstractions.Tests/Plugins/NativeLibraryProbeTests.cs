using System.Runtime.InteropServices;
using FluentAssertions;
using GeoAssets.Hosting.Abstractions.Plugins;
using Xunit;

namespace GeoAssets.Hosting.Abstractions.Tests.Plugins;

/// <summary>
/// XD01-427: <see cref="NativeLibraryProbe"/>, <see cref="PluginLoadContext"/>'s fallback for native libraries a
/// plugin's <c>.deps.json</c> doesn't list. These tests probe placeholder files for every platform's naming; the
/// real-library path is <see cref="PluginLoadContextTests"/>'.
/// </summary>
public sealed class NativeLibraryProbeTests : IDisposable
{
    private static readonly string[] Rids = ["linux-musl-x64", "linux-x64"];

    private readonly TempPluginDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("Linux", "gdal", new[] { "gdal.so", "libgdal.so", "gdal", "libgdal" })]
    [InlineData("Linux", "libgdal", new[] { "libgdal.so", "libgdal" })]
    [InlineData("Linux", "libgdal.so", new[] { "libgdal.so" })]
    [InlineData("Linux", "libgdal.so.36", new[] { "libgdal.so.36" })]
    [InlineData("Linux", "gdal.so", new[] { "gdal.so", "libgdal.so" })]
    [InlineData("MacOS", "gdal", new[] { "gdal.dylib", "libgdal.dylib", "gdal", "libgdal" })]
    [InlineData("MacOS", "libgdal.dylib", new[] { "libgdal.dylib" })]
    [InlineData("Windows", "gdal", new[] { "gdal.dll", "gdal" })]
    [InlineData("Windows", "gdal.DLL", new[] { "gdal.DLL" })]
    public void CandidateFileNames_FollowThePlatformsNaming(string platform, string name, string[] expected)
    {
        NativeLibraryProbe.CandidateFileNames(name, Enum.Parse<NativePlatform>(platform)).Should().Equal(expected);
    }

    [Fact]
    public void ProbeDirectories_AreThePluginFolder_ThenRuntimesRidNativePerIdentifier()
    {
        NativeLibraryProbe.ProbeDirectories("/plugins/gdal", Rids).Should().Equal(
            "/plugins/gdal",
            Path.Combine("/plugins/gdal", "runtimes", "linux-musl-x64", "native"),
            Path.Combine("/plugins/gdal", "runtimes", "linux-x64", "native"));
    }

    [Fact]
    public void ALibraryInThePluginFolder_WinsOverRuntimes()
    {
        var root = Touch("libgdal.so");
        Touch(Path.Combine("runtimes", "linux-x64", "native", "libgdal.so"));

        NativeLibraryProbe.Probe(_temp.Root, "gdal", NativePlatform.Linux, Rids).Should().Be(root);
    }

    [Fact]
    public void TheFirstRuntimeIdentifier_WinsOverTheNext()
    {
        Touch(Path.Combine("runtimes", "linux-x64", "native", "libgdal.so"));
        var musl = Touch(Path.Combine("runtimes", "linux-musl-x64", "native", "libgdal.so"));

        NativeLibraryProbe.Probe(_temp.Root, "gdal", NativePlatform.Linux, Rids).Should().Be(musl);
    }

    [Fact]
    public void ALibraryOnlyUnderAPortableRid_IsFound()
    {
        var portable = Touch(Path.Combine("runtimes", "linux-x64", "native", "libproj.so.25"));

        NativeLibraryProbe.Probe(_temp.Root, "libproj.so.25", NativePlatform.Linux, Rids).Should().Be(portable);
    }

    [Theory]
    [InlineData("MacOS", "libgdal.dylib")]
    [InlineData("Windows", "gdal.dll")]
    public void EachPlatformsFileName_IsFound(string platform, string fileName)
    {
        var path = Touch(Path.Combine("runtimes", "linux-x64", "native", fileName));

        NativeLibraryProbe.Probe(_temp.Root, "gdal", Enum.Parse<NativePlatform>(platform), Rids).Should().Be(path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/usr/lib/libgdal.so")]
    [InlineData("sub/libgdal.so")]
    [InlineData("missing")]
    public void APathOrAMissingLibrary_IsNotProbed(string name)
    {
        Touch(Path.Combine("sub", "libgdal.so"));

        NativeLibraryProbe.Probe(_temp.Root, name, NativePlatform.Linux, Rids).Should().BeNull();
    }

    [Fact]
    public void TheCurrentRuntimeIdentifiers_StartWithTheRunningOne_AndEndWithItsPortableForm()
    {
        var rids = NativeLibraryProbe.CurrentRuntimeIdentifiers();

        rids[0].Should().Be(RuntimeInformation.RuntimeIdentifier);
        rids[^1].Should().MatchRegex("^(win|osx|linux)-[a-z0-9]+$");
    }

    [Fact]
    public void TheCurrentPlatform_IsTheRunningOperatingSystem()
    {
        var expected = OperatingSystem.IsWindows() ? NativePlatform.Windows
            : OperatingSystem.IsMacOS() ? NativePlatform.MacOS
            : NativePlatform.Linux;

        NativeLibraryProbe.CurrentPlatform.Should().Be(expected);
    }

    private string Touch(string relativePath)
    {
        var path = Path.Combine(_temp.Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }
}
