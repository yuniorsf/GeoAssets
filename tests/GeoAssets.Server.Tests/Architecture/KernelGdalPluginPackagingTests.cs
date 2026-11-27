using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// XD01-459 — the kernel ships the GDAL plugin as a drop-in: its build output (built with this test project, in the same
/// configuration) carries <c>plugins/providers/GeoAssets.Provider.Gdal/</c> with the plugin's managed assemblies and
/// <c>.deps.json</c> and the natives of this machine's runtime only, and no static copy of GDAL beside the kernel. The
/// Linux publish of the Docker image is checked by the kernel's own <c>VerifyGdalProviderPluginPublished</c> target
/// (its RID's <c>libgdal_wrap.so</c>, no other RID's natives).
/// </summary>
[Trait("Category", "Architecture")]
public sealed class KernelGdalPluginPackagingTests
{
    private const string PluginName = "GeoAssets.Provider.Gdal";

    [Fact]
    public void TheKernelsBuildOutput_CarriesTheGdalPluginFolder_WithThisRuntimesNativesOnly()
    {
        var output = KernelOutputDirectory();
        var plugin = Path.Combine(output, "plugins", "providers", PluginName);

        File.Exists(Path.Combine(plugin, PluginName + ".dll")).Should().BeTrue("the plugin's entry assembly is in {0}", plugin);
        File.Exists(Path.Combine(plugin, PluginName + ".deps.json")).Should().BeTrue("its load context resolves its dependencies through it");
        File.Exists(Path.Combine(plugin, "MaxRev.Gdal.Core.dll")).Should().BeTrue("GDAL's bindings travel with the plugin");
        File.Exists(Path.Combine(plugin, "GeoAssets.Core.dll")).Should().BeFalse("the shared contracts are the host's");

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var rid = RuntimeInformation.RuntimeIdentifier;
            var native = Path.Combine(plugin, "runtimes", rid, "native");
            Directory.EnumerateFiles(native, "libgdal_wrap.*").Should().NotBeEmpty("{0}'s native GDAL is in {1}", rid, native);
            Directory.GetDirectories(Path.Combine(plugin, "runtimes")).Select(Path.GetFileName)
                .Should().BeSubsetOf([rid, "any"], "only the natives that can run on this machine are copied");
        }

        Directory.EnumerateFiles(output, "MaxRev.Gdal*.dll").Should().BeEmpty("the kernel never references GDAL statically");
        File.Exists(Path.Combine(output, PluginName + ".dll")).Should().BeFalse();
    }

    /// <summary><c>apps/GeoAssets.Server/bin/{configuration}/{framework}/</c>, the same one this test project's output is in.</summary>
    private static string KernelOutputDirectory()
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework = testOutput.Name;
        var configuration = testOutput.Parent!.Name;
        var root = testOutput;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GeoAssets.sln")))
            root = root.Parent;

        root.Should().NotBeNull("the test runs inside the repository");
        return Path.Combine(root!.FullName, "apps", "GeoAssets.Server", "bin", configuration, framework);
    }
}
