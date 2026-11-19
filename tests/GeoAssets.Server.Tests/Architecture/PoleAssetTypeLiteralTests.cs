using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// XD01-368: the seeded pole's asset type id is data, not code. Which asset types are poles is the catalog's to say,
/// by the <c>occupiable-support</c> role, so the literal id appears only in the PostgreSQL provider's seed data
/// (its migrations and the model data they mirror). The scan covers the production trees (<c>apps/</c>, <c>core/</c>,
/// <c>workflow/</c>, <c>providers/</c>, <c>plugins/</c>, <c>ipbilling/</c>, <c>projects/</c>); <c>tests/</c> and
/// <c>tools/</c> (benchmark tooling that seeds its own database) are not scanned.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class PoleAssetTypeLiteralTests
{
    private const string PoleAssetTypeGuid = "00000000-0000-0000-0000-000000000004";

    private static readonly string[] ScannedRoots = ["apps", "core", "workflow", "providers", "plugins", "ipbilling", "projects"];
    private static readonly string[] ScannedPatterns = ["*.cs", "*.razor", "*.js", "*.json"];
    private static readonly string[] SkippedDirectories = ["bin", "obj", "node_modules"];

    private static readonly string[] AllowedPrefixes =
    [
        "providers/GeoAssets.Provider.PostgreSQL/Migrations/",
        "providers/GeoAssets.Provider.PostgreSQL/Data/",
    ];

    [Fact]
    public void PoleAssetTypeGuidLiteral_AppearsOnlyInProviderSeedData()
    {
        var root = FindRepositoryRoot();

        var offenders = ScannedRoots
            .Select(scanned => Path.Combine(root, scanned))
            .Where(Directory.Exists)
            .SelectMany(Files)
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relative => !AllowedPrefixes.Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(relative => File.ReadAllText(Path.Combine(root, relative)).Contains(PoleAssetTypeGuid, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "the pole asset type is identified by the '{0}' role, not by its seeded id; found the id in: {1}",
            "occupiable-support",
            string.Join(", ", offenders));
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (var pattern in ScannedPatterns)
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.Ordinal)) continue;

            foreach (var file in Files(child))
                yield return file;
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln"))) return directory.FullName;
        }

        throw new InvalidOperationException($"No GeoAssets.sln found above '{AppContext.BaseDirectory}'.");
    }
}
