using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Architecture;

[Trait("Category", "Architecture")]
public class ArchitectureTests
{
    private static readonly Assembly CoreAssembly = typeof(GeoFeature).Assembly;

    [Fact]
    public void CoreAssembly_ShouldNotReference_InfrastructureOrPersistencePackages()
    {
        var referencedNames = CoreAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        referencedNames.Should().NotContain(
            name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal),
            because: "GeoAssets.Core is a pure domain layer and must not depend on EF Core");

        referencedNames.Should().NotContain(
            name => name.StartsWith("Npgsql", StringComparison.Ordinal),
            because: "GeoAssets.Core must not depend on PostgreSQL/PostGIS drivers directly");

        referencedNames.Should().NotContain(
            name => name.StartsWith("GeoAssets.Provider.", StringComparison.Ordinal),
            because: "Provider implementations depend on GeoAssets.Core, never the reverse");
    }

    [Fact]
    public void CoreAssembly_ShouldNotReference_ApplicationHosts()
    {
        var referencedNames = CoreAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        referencedNames.Should().NotContain([
            "GeoAssets.Shared",
            "GeoAssets.Web",
            "GeoAssets.Server",
            "GeoAssets.MAUI"
        ]);
    }

    [Fact]
    public void CoreAssetProviders_ShouldResideIn_ProvidersNamespace()
    {
        var providerTypes = CoreAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAssetProvider).IsAssignableFrom(t))
            .ToArray();

        providerTypes.Should().NotBeEmpty();
        providerTypes.Should().OnlyContain(
            t => string.Equals(t.Namespace, "GeoAssets.Core.Providers", StringComparison.Ordinal),
            because: "all concrete IAssetProvider decorators/proxies in GeoAssets.Core belong in GeoAssets.Core.Providers");
    }
}

