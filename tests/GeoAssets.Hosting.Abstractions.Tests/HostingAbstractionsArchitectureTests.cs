using Xunit;
using FluentAssertions;
using GeoAssets.Hosting.Abstractions;

namespace GeoAssets.Hosting.Abstractions.Tests;

[Trait("Category", "Architecture")]
public sealed class HostingAbstractionsArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "GeoAssets.Provider",
        "GeoAssets.Workflow",
        "GeoAssets.Domain",
        "GeoAssets.IpBilling",
        "GeoAssets.Server",
    ];

    [Fact]
    public void Assembly_DoesNotReference_EfCoreNpgsqlProviderOrVerticalAssemblies()
    {
        var referenced = typeof(IGeoAssetsModule).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        referenced.Should().BeEmpty();
    }
}
