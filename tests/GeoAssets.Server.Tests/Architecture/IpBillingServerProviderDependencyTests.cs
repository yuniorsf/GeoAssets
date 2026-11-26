using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// XD01-424 (ADR-002 DIP guardrail, following XD01-423): <c>GeoAssets.IpBilling.Server</c>'s endpoints and services
/// depend on the contracts in <c>GeoAssets.Core</c> (e.g. <c>IWorkbookReader</c>), never on a provider assembly.
/// Only the composition root, <c>Program.cs</c>, may name a <c>GeoAssets.Provider.*</c> namespace to register the
/// implementations it composes.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class IpBillingServerProviderDependencyTests
{
    private const string CompositionRoot = "Program.cs";

    private static readonly string[] SkippedDirectories = ["bin", "obj"];

    [GeneratedRegex(@"^\s*(global\s+)?using\s+(static\s+)?(\w+\s*=\s*)?GeoAssets\.Provider\.", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProviderUsing();

    [GeneratedRegex(@"^\s*(//|\*|/\*)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CommentLine();

    [GeneratedRegex(@"\bGeoAssets\.Provider\.\w+\.\w+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex QualifiedProviderReference();

    [Fact]
    public void IpBillingServerSources_OutsideTheCompositionRoot_DoNotDependOnAProvider()
    {
        var server = Path.Combine(FindRoot(), "apps", "GeoAssets.IpBilling.Server");

        var sources = Directory.EnumerateFiles(server, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(server, path))
            .Where(relative => !relative.Split(Path.DirectorySeparatorChar)
                .Any(segment => SkippedDirectories.Contains(segment, StringComparer.Ordinal)))
            .ToList();
        sources.Should().Contain(["IpBillingFiscalizacaoRestApiExtensions.cs", "IpBillingLevantamentoRestApiExtensions.cs",
            "IpBillingOficioRestApiExtensions.cs", CompositionRoot], "the scan must actually find the IpBilling.Server sources");

        var offenders = sources
            .Where(relative => !string.Equals(relative, CompositionRoot, StringComparison.Ordinal))
            .SelectMany(relative => File.ReadLines(Path.Combine(server, relative))
                .Select((line, index) => (relative, line, number: index + 1)))
            .Where(hit => DependsOnAProvider(hit.line))
            .Select(hit => $"{hit.relative}:{hit.number}: {hit.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty("IpBilling.Server's endpoints consume GeoAssets.Core contracts; only Program.cs composes providers");
    }

    [Fact]
    public void TheCompositionRoot_IsWhereTheProvidersAreComposed()
    {
        var program = Path.Combine(FindRoot(), "apps", "GeoAssets.IpBilling.Server", CompositionRoot);

        File.ReadLines(program).Should().Contain(line => ProviderUsing().IsMatch(line),
            "the exemption only means something while Program.cs is the file that names the providers");
    }

    [Theory]
    [InlineData("using GeoAssets.Provider.Xlsx;")]
    [InlineData("global using GeoAssets.Provider.Rest;")]
    [InlineData("using static GeoAssets.Provider.Xlsx.ClosedXmlWorkbookReader;")]
    [InlineData("using Reader = GeoAssets.Provider.Xlsx.ClosedXmlWorkbookReader;")]
    [InlineData("    var reader = new GeoAssets.Provider.Xlsx.ClosedXmlWorkbookReader();")]
    public void ThePatternCatchesEveryProviderDependencyShape(string line)
    {
        DependsOnAProvider(line).Should().BeTrue();
    }

    [Theory]
    [InlineData("using GeoAssets.Core.Interfaces;")]
    [InlineData("/// <see cref=\"GeoAssets.Provider.Xlsx.ClosedXmlWorkbookReader\"/> implements it.")]
    [InlineData("// GeoAssets.Provider.Xlsx.ClosedXmlWorkbookReader is registered by Program.cs")]
    public void ThePatternIgnoresCoreImportsAndComments(string line)
    {
        DependsOnAProvider(line).Should().BeFalse();
    }

    private static bool DependsOnAProvider(string line) =>
        ProviderUsing().IsMatch(line) || (!CommentLine().IsMatch(line) && QualifiedProviderReference().IsMatch(line));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln"))) return directory.FullName;
        }

        throw new InvalidOperationException($"No GeoAssets.sln found above '{AppContext.BaseDirectory}'.");
    }
}
