using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>XD01-384: the routing knows no vertical — its routes are whatever the registry holds.</summary>
public sealed class DescriptorRoutedArchitectureTests
{
    private static string RoutingDirectory() => Path.Combine(RepositoryRoot(), "workflow", "GeoAssets.Workflow.Rest", "Routing");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the solution root is above the test output");
        return directory!.FullName;
    }

    [Fact]
    public void TheVerticalsRoutingSources_AreFound_AndAreNotTheChassis()
    {
        var chassis = Directory.EnumerateFiles(RoutingDirectory(), "*.cs").Select(Path.GetFileName).ToList();
        var vertical = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "verticals", "NeutralOperator", "GeoAssets.NeutralOperator.Rest", "Routing"), "*.cs")
            .Select(Path.GetFileName).ToList();

        vertical.Should().HaveCount(2).And.OnlyContain(name => name!.StartsWith("DescriptorRouted", StringComparison.Ordinal));
        chassis.Should().NotIntersectWith(vertical, "XD01-388 moved the vertical's routed clients out of the chassis");
    }

    [Fact]
    public void TheRoutingSources_AreFound()
    {
        Directory.EnumerateFiles(RoutingDirectory(), "*.cs").Should().HaveCountGreaterThanOrEqualTo(8);
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
