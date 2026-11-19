using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using Xunit;

namespace GeoAssets.Core.Tests.Architecture;

[Trait("Category", "Architecture")]
public class ProviderFactoryContractsRetiredTests
{
    private static readonly string[] RetiredContracts = ["IExternalProviderFactory", "IAsyncProviderFactory"];

    [Fact]
    public void ExternalProviderFactoryContracts_AreRetired()
    {
        var assemblies = new List<Assembly> { typeof(IAssetProvider).Assembly };
        assemblies.AddRange(Directory.EnumerateFiles(AppContext.BaseDirectory, "GeoAssets.*.dll")
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path))));

        var defined = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => RetiredContracts.Contains(type.Name, StringComparer.Ordinal))
            .Select(type => type.FullName);

        defined.Should().BeEmpty("XD01-371 retired the factory contracts in favour of IProviderPlugin");
    }
}
