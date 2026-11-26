using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Shared.Tests.Architecture;

/// <summary>
/// XD01-422 (ADR-002 invariant "zero <c>is IWmsProvider</c> in UI/map components"): <c>GeoAssets.Shared</c>
/// decides how to draw a provider from its capabilities through the <c>IProviderMapLayerRenderer</c> chain — no
/// source outside <c>Services/Rendering/</c> (the strategies themselves) tests a provider for
/// <c>IWmsProvider</c>, so a new kind of provider never needs a new branch in the map or the pool panel.
/// This is also XD01-424's guardrail of zero <c>is [not] IWmsProvider</c> in <c>GeoAssets.Shared/Components/</c>: the
/// scan covers every component, and the components that used to branch on it must stay in the scan.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class ProviderRenderDispatchTests
{
    private static readonly string[] SkippedDirectories = ["bin", "obj"];

    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln was not found above the test output directory.");
    }

    [GeneratedRegex(@"\b(is|as)\s+(not\s+)?IWmsProvider\b|\bcase\s+IWmsProvider\b|\bIWmsProvider\s+\w+\s*=>|\bIWmsProvider\s*=>", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WmsTypeTest();

    [Fact]
    public void NoSharedSourceOutsideTheRenderers_TypeTestsForIWmsProvider()
    {
        var shared = Path.Combine(Root, "apps", "GeoAssets.Shared");
        var rendering = Path.Combine("Services", "Rendering") + Path.DirectorySeparatorChar;

        var sources = Directory.EnumerateFiles(shared, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".razor", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(shared, path))
            .Where(relative => !relative.Split(Path.DirectorySeparatorChar)
                .Any(segment => SkippedDirectories.Contains(segment, StringComparer.Ordinal)))
            .ToList();
        sources.Should().Contain(
            [
                Path.Combine("Components", "Map", "MapContainer.razor.cs"),
                Path.Combine("Components", "Providers", "ProviderPoolPanel.razor"),
                Path.Combine("Components", "Providers", "ProviderPoolPanel.razor.cs"),
            ],
            "the scan must actually find the Shared components that used to type-test for IWmsProvider");

        var offenders = sources
            .Where(relative => !relative.StartsWith(rendering, StringComparison.Ordinal))
            .SelectMany(relative => File.ReadLines(Path.Combine(shared, relative))
                .Select((line, index) => (relative, line, number: index + 1)))
            .Where(hit => WmsTypeTest().IsMatch(hit.line))
            .Select(hit => $"{hit.relative}:{hit.number}: {hit.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty("render dispatch goes through IProviderMapLayerRenderer and IProviderCapabilities");
    }

    [Theory]
    [InlineData("if (entry.Provider is IWmsProvider wms)")]
    [InlineData("e.Provider is not IWmsProvider && ShouldShowOnMap(e)")]
    [InlineData("var wms = entry.Provider as IWmsProvider;")]
    [InlineData("case IWmsProvider w:")]
    [InlineData("IWmsProvider => ProviderRenderCapabilities.RasterTiles,")]
    public void ThePatternCatchesEveryTypeTestShape(string line)
    {
        WmsTypeTest().IsMatch(line).Should().BeTrue();
    }

    [Theory]
    [InlineData("/// <summary>An <see cref=\"IWmsProvider\"/> layer.</summary>")]
    [InlineData("public sealed class WmsAssetProvider : IAssetProvider, IWmsProvider")]
    public void ThePatternIgnoresMentionsThatAreNotTypeTests(string line)
    {
        WmsTypeTest().IsMatch(line).Should().BeFalse();
    }
}
