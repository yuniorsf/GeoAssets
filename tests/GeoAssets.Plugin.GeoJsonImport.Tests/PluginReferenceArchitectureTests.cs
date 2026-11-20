using Xunit;
using FluentAssertions;

namespace GeoAssets.Plugin.GeoJsonImport.Tests;

public sealed class PluginReferenceArchitectureTests
{
    [Fact]
    public void PluginAssembly_ReferencesNoPostgresOrEfCoreAssembly()
    {
        var referenced = typeof(GeoJsonImportHandler).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        referenced.Should().NotContain("GeoAssets.Provider.PostgreSQL");
        referenced.Should().NotContain(n => n.StartsWith("Npgsql", StringComparison.Ordinal));
        referenced.Should().NotContain(n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
