using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using Xunit;

namespace GeoAssets.Provider.PluginModules.Tests;

/// <summary>
/// XD01-425 (ADR-002 D1/D7): every spatial provider assembly — every project under <c>providers/</c> that ships an
/// <see cref="IProviderPlugin"/> — exposes exactly one public, concrete <see cref="IProviderPluginModule"/>, and no
/// two modules share an id (discovery fails fast on a duplicate).
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class ProviderPluginModuleAssemblyContractTests
{
    private static readonly Assembly[] ProviderAssemblies =
        [.. ProviderModuleCases.ById.Values.Select(c => c.ModuleType.Assembly).Distinct()];

    [Fact]
    public void EveryProviderProjectShippingAProviderPlugin_IsCovered()
    {
        var providersDirectory = Path.Combine(FindRepositoryRoot(), "providers");
        var projectsWithAPlugin = Directory
            .EnumerateDirectories(providersDirectory)
            .Where(project => Directory
                .EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsBuildOutput(f))
                .Any(f => ImplementsProviderPlugin().IsMatch(File.ReadAllText(f))))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        projectsWithAPlugin.Should().NotBeEmpty("the scan must see the provider projects");
        ProviderAssemblies.Select(a => a.GetName().Name).Order(StringComparer.Ordinal)
            .Should().Equal(projectsWithAPlugin, "every provider project that ships an IProviderPlugin needs a module");
    }

    [Fact]
    public void EveryProviderAssembly_ExposesExactlyOnePublicConcreteModule()
    {
        foreach (var assembly in ProviderAssemblies)
        {
            var modules = assembly.GetTypes()
                .Where(t => typeof(IProviderPluginModule).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
                .ToArray();

            modules.Should().ContainSingle("{0} must expose exactly one IProviderPluginModule", assembly.GetName().Name)
                .Which.IsPublic.Should().BeTrue("discovery scans public module types");
        }
    }

    [Fact]
    public void ModuleIds_AreUniqueIgnoringCase()
    {
        var ids = ProviderAssemblies
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => typeof(IProviderPluginModule).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .Select(t => ((IProviderPluginModule)Activator.CreateInstance(t)!).PluginId)
            .ToArray();

        ids.Should().HaveCount(ProviderAssemblies.Length);
        ids.Should().OnlyHaveUniqueItems(id => id.ToUpperInvariant());
        ids.Should().AllSatisfy(id => id.Should().NotBeNullOrWhiteSpace());
    }

    [GeneratedRegex(@"[:,]\s*IProviderPlugin\b(?!Module)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImplementsProviderPlugin();

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
