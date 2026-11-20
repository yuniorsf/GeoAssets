using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>XD01-384: the routing knows no vertical — its routes are whatever the registry holds.</summary>
public sealed class DescriptorRoutedArchitectureTests
{
    private static string RoutingDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the solution root is above the test output");
        return Path.Combine(directory!.FullName, "workflow", "GeoAssets.Workflow.Rest", "Routing");
    }

    [Fact]
    public void TheRoutingSources_AreFound()
    {
        Directory.EnumerateFiles(RoutingDirectory(), "*.cs").Should().HaveCountGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void NoRoutingSource_HasAStringLiteral_NamingAVerticalOrAVerticalRoute()
    {
        var literal = new Regex("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.None, TimeSpan.FromSeconds(5));
        var offenders = Directory.EnumerateFiles(RoutingDirectory(), "*.cs")
            .SelectMany(file => File.ReadAllLines(file)
                .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .SelectMany(line => literal.Matches(line).Select(m => m.Value))
                .Where(value => value.Contains("neutral", StringComparison.OrdinalIgnoreCase) || value.Contains("/api/v/", StringComparison.OrdinalIgnoreCase))
                .Select(value => $"{Path.GetFileName(file)}: {value}"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void NoRoutingSource_NamesAVerticalsTypeOutsideItsUsings()
    {
        var offenders = Directory.EnumerateFiles(RoutingDirectory(), "*.cs")
            .SelectMany(file => File.ReadAllLines(file)
                .Where(line => !line.StartsWith("using ", StringComparison.Ordinal) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .Where(line => line.Contains("NeutralOperator", StringComparison.OrdinalIgnoreCase))
                .Select(line => $"{Path.GetFileName(file)}: {line.Trim()}"))
            .ToList();

        offenders.Should().BeEmpty();
    }
}
