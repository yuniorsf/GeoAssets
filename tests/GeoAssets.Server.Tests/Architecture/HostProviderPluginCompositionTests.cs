using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// XD01-428 (ADR-002 D1/D2): the hosts compose their provider plugins through discovery — the Web host for the browser and
/// MAUI for mobile/desktop from trimming-safe static sources, the Server from its plugin directory — and no host calls a
/// provider's plugin extension the plugin modules replace (doing both registers the plugin twice and fails the startup).
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class HostProviderPluginCompositionTests
{
    [GeneratedRegex(
        @"\bAddGeoAssets(Rest|Wfs|Wms|Shapefile|ShapefilePlugin|Postgres)\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex ProviderPluginExtensionCall();

    [Fact]
    public void NoHost_CallsAProviderPluginExtension_TheModulesReplace()
    {
        var apps = Path.Combine(FindRepositoryRoot(), "apps");
        var sources = Sources(apps);

        sources.Should().NotBeEmpty("the scan must see the hosts' sources");
        var offenders = sources
            .Where(f => ProviderPluginExtensionCall().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(apps, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "hosts register provider plugins through their IProviderPluginModule (AddDiscoveredProviderPlugins / " +
            "AddProviderPluginsFromDirectory); offenders: {0}",
            string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("GeoAssets.Web/Extensions/WebProviderPluginExtensions.cs", "ProviderHostTargets.Browser")]
    [InlineData("GeoAssets.MAUI/MauiProgram.cs", "ProviderHostTargets.MobileDesktop")]
    public void AClientHost_DiscoversItsBundledModules_ForItsOwnHostTarget(string compositionRoot, string hostTarget)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "apps", compositionRoot));

        text.Should().Contain("AddDiscoveredProviderPlugins(").And.Contain(hostTarget).And.Contain("new StaticAssemblyPluginSource()");
    }

    [Fact]
    public void TheServer_DiscoversItsPluginDirectory_ForTheServerHost()
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "apps", "GeoAssets.Server", "Program.cs"));

        text.Should().Contain("AddProviderPluginsFromDirectory(builder.Configuration, ProviderHostTargets.Server)");
    }

    [Theory]
    [InlineData("GeoAssets.Web")]
    [InlineData("GeoAssets.MAUI")]
    [InlineData("GeoAssets.Shared")]
    public void TrimmedClients_NeitherScanAssembliesNorReadAPluginDirectory(string project)
    {
        var offenders = Sources(Path.Combine(FindRepositoryRoot(), "apps", project))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains(".AddAssembly(", StringComparison.Ordinal)
                    || text.Contains("AddProviderPluginsFromDirectory", StringComparison.Ordinal)
                    || text.Contains("DirectoryPluginAssemblySource", StringComparison.Ordinal);
            })
            .Select(f => Path.GetFileName(f))
            .ToArray();

        offenders.Should().BeEmpty(
            "Blazor WebAssembly and MAUI name their provider modules with StaticAssemblyPluginSource.Add<TModule>() (ADR-002 D2); offenders: {0}",
            string.Join(", ", offenders));
    }

    private static string[] Sources(string directory) =>
    [
        .. Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)),
    ];

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln not found above the test output directory.");
    }
}
