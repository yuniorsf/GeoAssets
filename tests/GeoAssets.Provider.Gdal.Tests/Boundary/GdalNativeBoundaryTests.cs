using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests.Boundary;

/// <summary>
/// ADR-002 D5 (XD01-437) — the single native GDAL boundary: GDAL's managed bindings and native runtime come into the
/// repository through <c>GeoAssets.Provider.Gdal</c> alone, <c>GdalBase</c> (and its <c>ConfigureAll</c>) is called
/// nowhere else — every other GDAL user goes through <see cref="GdalNativeInitializer"/> — and every host has one loading
/// model for the plugin: statically referenced (FieldService.Server, the network import CLI and its Ingestion.Proj) or
/// loaded from a plugin directory (a server's <c>ProviderPlugin</c> item), never both.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class GdalNativeBoundaryTests
{
    private const string GdalProjectDirectory = "providers/GeoAssets.Provider.Gdal";
    private const string GdalProjectFile = "GeoAssets.Provider.Gdal.csproj";

    private static readonly string[] ScannedRoots =
        ["apps", "core", "providers", "plugins", "verticals", "workflow", "ipbilling", "projects", "tools", "tests", "examples"];

    private static readonly string[] StaticHosts =
    [
        "apps/GeoAssets.FieldService.Server/GeoAssets.FieldService.Server.csproj",
        "providers/GeoAssets.Ingestion.Proj/GeoAssets.Ingestion.Proj.csproj",
        "tools/GeoAssets.Tools.NetworkImport/GeoAssets.Tools.NetworkImport.csproj",
        "tests/GeoAssets.Provider.Gdal.Tests/GeoAssets.Provider.Gdal.Tests.csproj",
    ];

    // Built from parts so this file's own code doesn't match its scan.
    [GeneratedRegex(@"\bGdal" + "Base\\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex GdalBaseUse();

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex Comment();

    [Fact]
    public void OnlyTheGdalProject_ReferencesAMaxRevGdalPackage()
    {
        var offenders = Projects()
            .Where(p => !p.RelativePath.StartsWith(GdalProjectDirectory + "/", StringComparison.Ordinal))
            .Where(p => p.Items("PackageReference").Any(include => include.StartsWith("MaxRev.Gdal", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.RelativePath)
            .ToArray();

        offenders.Should().BeEmpty(
            "GDAL's bindings and native runtime come in through {0} only (ADR-002 D5); reference that project instead. Offenders: {1}",
            GdalProjectFile,
            string.Join(", ", offenders));
        Projects().Single(p => string.Equals(p.RelativePath, $"{GdalProjectDirectory}/{GdalProjectFile}", StringComparison.Ordinal))
            .Items("PackageReference").Should().Contain("MaxRev.Gdal.Core");
    }

    [Fact]
    public void NoSourceOutsideTheGdalProject_TouchesGdalBase()
    {
        var root = RepositoryRoot();
        var offenders = ScannedRoots
            .Select(r => Path.Combine(root, r))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(f))
            .Select(f => (Path: Relative(root, f), Text: Code(f)))
            .Where(f => !f.Path.StartsWith(GdalProjectDirectory + "/", StringComparison.Ordinal))
            .Where(f => GdalBaseUse().IsMatch(f.Text))
            .Select(f => f.Path)
            .ToArray();

        offenders.Should().BeEmpty(
            "native GDAL is configured once, by GdalNativeInitializer.EnsureInitialized() (ADR-002 D5); offenders: {0}",
            string.Join(", ", offenders));
    }

    [Fact]
    public void OnlyGdalNativeInitializer_CallsConfigureAll_InsideTheGdalProject()
    {
        var root = RepositoryRoot();
        var callers = Directory.EnumerateFiles(Path.Combine(root, GdalProjectDirectory), "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .Where(f => Regex.IsMatch(Code(f), @"\bGdal" + @"Base\s*\.\s*ConfigureAll\s*\(", RegexOptions.None, TimeSpan.FromSeconds(5)))
            .Select(f => Path.GetFileName(f))
            .ToArray();

        callers.Should().Equal("GdalNativeInitializer.cs");
    }

    [Fact]
    public void EveryHost_HasOneLoadingModel_ForTheGdalPlugin()
    {
        var projects = Projects();
        var staticReferrers = projects.Where(p => p.References("ProjectReference")).Select(p => p.RelativePath).ToArray();
        var pluginShippers = projects.Where(p => p.References("ProviderPlugin")).Select(p => p.RelativePath).ToArray();

        staticReferrers.Should().BeEquivalentTo(StaticHosts,
            "a new static referrer of {0} must be one loading model only — review it and list it here", GdalProjectFile);
        staticReferrers.Intersect(pluginShippers, StringComparer.Ordinal).Should().BeEmpty(
            "a host loads managed GDAL once: statically or from its plugin directory, never both");
        staticReferrers.Should().NotContain("apps/GeoAssets.Server/GeoAssets.Server.csproj",
            "the kernel composes provider plugins from its plugin directory (AddProviderPluginsFromDirectory) — GDAL is a drop-in there");
    }

    [Fact]
    public void AStaticHost_NeverReadsAProviderPluginDirectory()
    {
        var root = RepositoryRoot();
        var offenders = StaticHosts
            .Where(p => p.StartsWith("apps/", StringComparison.Ordinal))
            .Select(p => Path.Combine(root, Path.GetDirectoryName(p)!))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(f))
            .Where(f => File.ReadAllText(f).Contains("AddProviderPluginsFromDirectory", StringComparison.Ordinal)
                        || File.ReadAllText(f).Contains("DirectoryPluginAssemblySource", StringComparison.Ordinal))
            .Select(f => Relative(root, f))
            .ToArray();

        offenders.Should().BeEmpty(
            "a host that references GeoAssets.Provider.Gdal statically would load a second managed copy from a plugin directory; offenders: {0}",
            string.Join(", ", offenders));
    }

    private static List<ProjectFile> Projects()
    {
        var root = RepositoryRoot();
        return [.. ScannedRoots
            .Select(r => Path.Combine(root, r))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.AllDirectories))
            .Where(p => !IsBuildOutput(p))
            .Select(p => new ProjectFile(Relative(root, p), XDocument.Load(p)))];
    }

    /// <summary>A source file's text without its comments, so documentation that names GDAL's API doesn't count as using it.</summary>
    private static string Code(string path) => Comment().Replace(File.ReadAllText(path), string.Empty);

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln not found above the test output directory.");
    }

    private sealed record ProjectFile(string RelativePath, XDocument Document)
    {
        public IEnumerable<string> Items(string itemName) =>
            Document.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, itemName, StringComparison.Ordinal))
                .Select(e => (string?)e.Attribute("Include"))
                .OfType<string>();

        public bool References(string itemName) =>
            Items(itemName).Any(include => string.Equals(
                Path.GetFileName(include.Replace('\\', '/')), GdalProjectFile, StringComparison.OrdinalIgnoreCase));
    }
}
