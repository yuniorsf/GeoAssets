using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// XD01-386, Invariant 1 — Core Database Encapsulation: the core asset database is opened by
/// <c>GeoAssets.Server</c> alone. Any other host reads core assets over the Core GeoAssets Server's
/// API (<c>IGeoCoreClient</c>), so no other production project may reference
/// <c>GeoAssets.Provider.PostgreSQL</c> (the project that owns <c>GeoAssetsDbContext</c>).
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ProviderPostgreSqlReferenceTests
{
    private const string ProviderProjectFile = "GeoAssets.Provider.PostgreSQL.csproj";
    private const string AllowedProject = "apps/GeoAssets.Server/GeoAssets.Server.csproj";

    private static readonly string[] ScannedRoots =
        ["apps", "verticals", "workflow", "core", "providers", "plugins", "ipbilling", "projects", "tools"];

    /// <summary>
    /// Developer tooling that is not a deployed service host: command-line benchmarks/importers that
    /// connect to a database directly by design. Tools-only exemption — not an allowlist anything
    /// else may join.
    /// </summary>
    private static readonly string[] ToolsOnlyExemptions =
    [
        "tools/GeoAssets.Bench.GetPageAsync/GeoAssets.Bench.GetPageAsync.csproj",
        "tools/GeoAssets.Tools.SharedUseImport/GeoAssets.Tools.SharedUseImport.csproj",
    ];

    /// <summary>
    /// Pre-existing references found when this test was written (not introduced by XD01-386), each
    /// a violation of the invariant that still has to be removed. Ratchet: this list may only
    /// shrink — <see cref="KnownViolations_AreStillViolations_SoTheListOnlyShrinks"/> fails once an
    /// entry stops referencing the provider, and nothing may be added.
    /// </summary>
    private static readonly string[] KnownPreExistingViolations =
    [
        "plugins/commands/GeoAssets.Plugin.GeoJsonImport/GeoAssets.Plugin.GeoJsonImport.csproj",
    ];

    [Fact]
    public void OnlyGeoAssetsServerReferencesProviderPostgreSql()
    {
        var referrers = FindReferrers();

        referrers.Should().Contain(AllowedProject, "the scan must see the one host that is allowed to open the core database");

        var offenders = referrers
            .Except([AllowedProject], StringComparer.Ordinal)
            .Except(ToolsOnlyExemptions, StringComparer.Ordinal)
            .Except(KnownPreExistingViolations, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "only {0} may reference {1} (Invariant 1); other hosts read core assets through IGeoCoreClient. Offenders: {2}",
            AllowedProject,
            ProviderProjectFile,
            string.Join(", ", offenders));
    }

    [Fact]
    public void FieldServiceServerProject_DoesNotReferenceProviderPostgreSql()
    {
        FindReferrers().Should().NotContain("apps/GeoAssets.FieldService.Server/GeoAssets.FieldService.Server.csproj");
    }

    [Fact]
    public void KnownViolations_AreStillViolations_SoTheListOnlyShrinks()
    {
        var referrers = FindReferrers();

        KnownPreExistingViolations.Except(referrers, StringComparer.Ordinal).Should().BeEmpty(
            "an entry that no longer references {0} must be removed from {1}, so the coupling cannot return",
            ProviderProjectFile,
            nameof(KnownPreExistingViolations));
        ToolsOnlyExemptions.Except(referrers, StringComparer.Ordinal).Should().BeEmpty(
            "a tool that no longer references {0} must be removed from {1}",
            ProviderProjectFile,
            nameof(ToolsOnlyExemptions));
    }

    [Fact]
    public void FieldServiceServer_DoesNotReferenceGeoAssetsDbContext()
    {
        var project = Path.Combine(FindRepositoryRoot(), "apps", "GeoAssets.FieldService.Server");
        var files = Directory
            .EnumerateFiles(project, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".csproj", StringComparison.Ordinal))
            .Where(f => !IsBuildOutput(f))
            .ToArray();

        files.Should().NotBeEmpty("the scan must see the FieldService host's sources");

        var offenders = files
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("GeoAssetsDbContext", StringComparison.Ordinal)
                    || text.Contains("Provider.PostgreSQL", StringComparison.Ordinal);
            })
            .Select(f => Path.GetRelativePath(project, f))
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "GeoAssets.FieldService.Server never opens the core asset database (Invariant 1); offenders: {0}",
            string.Join(", ", offenders));
    }

    private static string[] FindReferrers()
    {
        var root = FindRepositoryRoot();
        var referrers = new List<string>();

        foreach (var scanned in ScannedRoots)
        {
            var directory = Path.Combine(root, scanned);
            if (!Directory.Exists(directory)) continue;

            foreach (var project in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories).Where(p => !IsBuildOutput(p)))
            {
                var projectDirectory = Path.GetDirectoryName(project)!;
                var references = XDocument.Load(project)
                    .Descendants("ProjectReference")
                    .Select(e => (string?)e.Attribute("Include"))
                    .OfType<string>()
                    .Select(include => Path.GetFileName(Path.GetFullPath(Path.Combine(projectDirectory, include.Replace('\\', '/')))));

                if (references.Any(name => string.Equals(name, ProviderProjectFile, StringComparison.OrdinalIgnoreCase)))
                    referrers.Add(Path.GetRelativePath(root, project).Replace('\\', '/'));
            }
        }

        return [.. referrers.Order(StringComparer.Ordinal)];
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln not found above the test output directory.");
    }
}
