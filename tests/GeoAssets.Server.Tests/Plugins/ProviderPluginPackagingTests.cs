using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Plugins;

/// <summary>
/// XD01-428 (ADR-002 D2): <c>build/GeoAssets.ProviderPlugins.targets</c> — the opt-in packaging of drop-in provider
/// plugins — lays a listed plugin's whole build output out in the folder-per-plugin layout
/// <c>DirectoryPluginAssemblySource</c> reads, without referencing it or copying the shared contracts; and the Server
/// imports it, its Docker image's restore layer included.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ProviderPluginPackagingTests
{
    private const string TargetsFile = "build/GeoAssets.ProviderPlugins.targets";
    private const string FixturePluginName = "GeoAssets.PluginFixtures.Beta";

    private static string FixturePluginFolder =>
        Path.Combine(AppContext.BaseDirectory, "provider-plugin-fixtures", FixturePluginName);

    [Fact]
    public void ThePackagedPlugin_HasItsEntryAssemblyDepsJsonAndPrivateDependencies_InItsOwnFolder()
    {
        Directory.Exists(FixturePluginFolder).Should().BeTrue();
        File.Exists(Path.Combine(FixturePluginFolder, $"{FixturePluginName}.dll")).Should().BeTrue();
        File.Exists(Path.Combine(FixturePluginFolder, $"{FixturePluginName}.deps.json")).Should().BeTrue();
        File.Exists(Path.Combine(FixturePluginFolder, $"{FixturePluginName}.runtimeconfig.json")).Should().BeTrue(
            "the plugin is built with EnableDynamicLoading");
        File.Exists(Path.Combine(FixturePluginFolder, "GeoAssets.PluginFixtures.PrivateDependency.dll")).Should().BeTrue(
            "a plugin's private dependencies travel with it");
    }

    [Theory]
    [InlineData("GeoAssets.Core.dll")]
    [InlineData("GeoAssets.Identity.dll")]
    [InlineData("GeoAssets.Hosting.Abstractions.dll")]
    public void ThePackagedPlugin_DoesNotCarryTheSharedContracts(string sharedContract)
    {
        File.Exists(Path.Combine(FixturePluginFolder, sharedContract)).Should().BeFalse(
            "the host's copy of {0} is the one every plugin binds to (ADR-002 D3)", sharedContract);
    }

    [Fact]
    public void ThePackagedPlugin_IsNotReferencedByTheHost()
    {
        File.Exists(Path.Combine(AppContext.BaseDirectory, $"{FixturePluginName}.dll")).Should().BeFalse(
            "a drop-in plugin's assemblies must never load into the host's own context");
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GeoAssets.Server.Tests.deps.json"))
            .Should().NotContain(FixturePluginName);
    }

    [Fact]
    public void TheServer_ImportsThePackagingTargets()
    {
        var root = FindRepositoryRoot();
        var project = Path.Combine(root, "apps", "GeoAssets.Server", "GeoAssets.Server.csproj");

        var imports = XDocument.Load(project).Root!.Elements("Import")
            .Select(e => (string?)e.Attribute("Project"))
            .OfType<string>()
            .Select(p => Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, p.Replace('\\', '/'))))
                .Replace('\\', '/'));

        imports.Should().Contain(TargetsFile);
    }

    [Fact]
    public void TheServerDockerfile_RestoresWithThePackagingTargets()
    {
        var dockerfile = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "apps", "GeoAssets.Server", "Dockerfile"));

        dockerfile.Should().Contain(line => line.StartsWith("COPY --parents ", StringComparison.Ordinal)
                && line.Contains("**/*.csproj", StringComparison.Ordinal)
                && line.Contains(TargetsFile, StringComparison.Ordinal),
            "the restore layer copies only the project files and the build files they import");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln not found above the test output directory.");
    }
}
