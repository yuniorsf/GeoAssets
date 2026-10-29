using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests;

[Trait("Category", "Architecture")]
public sealed class ServerArchitectureTests
{
    [Fact]
    public void ServerAssembly_ShouldNotReference_IpBillingVerticalAssemblies()
    {
        var referencedNames = typeof(GeoAssetsRestApiExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        referencedNames.Should().NotContain(
            name => name.StartsWith("GeoAssets.IpBilling", StringComparison.Ordinal),
            because: "the Core Server (GeoAssets.Server) owns geographical features, alphanumeric attributes, and topological relations, and must not depend on the GEOS_FAT_IP vertical domain");
    }
}

